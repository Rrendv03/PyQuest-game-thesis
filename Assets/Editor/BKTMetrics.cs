using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Metric implementations for BKT predictive validation.
///
/// Definitions (plain language):
/// - AUC-ROC (Area Under the Receiver Operating Characteristic Curve):
///   0-1 score of how well predicted probabilities RANK correct responses
///   above incorrect ones. 0.5 = coin flip, 1.0 = perfect ranking.
///   Computed here as the rank statistic (Mann-Whitney U) with mid-ranks
///   for ties — this is the standard equivalent of sklearn's roc_auc_score.
/// - RMSE (Root Mean Squared Error): sqrt of the average squared gap between
///   predicted probability and the actual 0/1 outcome. Lower is better;
///   ~0.42-0.46 is typical for standard BKT on the ASSISTments/Cognitive
///   Tutor benchmarks. Note: RMSE is the metric the KT literature considers
///   primary for model comparison (Pelánek 2018, cited in pyBKT-examples).
/// - MAE (Mean Absolute Error): average absolute gap between prediction and
///   outcome. More robust to occasional large misses than RMSE.
/// - Accuracy: share of predictions where (P(correct) >= 0.5) matches the
///   actual outcome. pyBKT uses the same 0.5 threshold.
///
/// All four are computed on the SAME (prediction, outcome) pairs: for each
/// response, the prediction is P(correct) = P(L)(1-S) + (1-L)*G evaluated on
/// the mastery state BEFORE that response was observed.
/// </summary>
public static class BKTMetrics
{
    /// <summary>AUC-ROC via the rank statistic with mid-rank tie handling.</summary>
    public static double ComputeAucRoc(IList<double> predictions, IList<int> outcomes)
    {
        int n = predictions.Count;
        if (n == 0) return double.NaN;

        // Count positives/negatives; AUC is undefined if one class is absent.
        int nPos = 0, nNeg = 0;
        for (int i = 0; i < n; i++)
        {
            if (outcomes[i] == 1) nPos++;
            else nNeg++;
        }
        if (nPos == 0 || nNeg == 0) return double.NaN;

        // Sort indices by prediction value (ascending).
        var order = Enumerable.Range(0, n).OrderBy(i => predictions[i]).ToArray();

        // Assign mid-ranks to tied groups.
        double[] ranks = new double[n];
        int i0 = 0;
        while (i0 < n)
        {
            int i1 = i0;
            while (i1 + 1 < n && predictions[order[i1 + 1]] == predictions[order[i0]])
                i1++;
            double midRank = (i0 + i1) / 2.0 + 1.0; // 1-based average rank
            for (int k = i0; k <= i1; k++)
                ranks[order[k]] = midRank;
            i0 = i1 + 1;
        }

        double rankSumPos = 0.0;
        for (int i = 0; i < n; i++)
            if (outcomes[i] == 1) rankSumPos += ranks[i];

        // AUC = (R_pos - n_pos(n_pos+1)/2) / (n_pos * n_neg)
        //
        // BUG FIX (2026-10-07, caught by the user's first real ASSISTments run,
        // logged 2026-10-08): the old code computed the baseline rank sum in INT
        // arithmetic; n_pos > 46,341 makes nPos*(nPos+1) overflow int32. The
        // pooled pass hit n_pos = 130,456 -> product 17,018,898,392 wrapped to
        // -160,970,792, int /2 gave -80,485,396, so the subtraction silently
        // became an ADDITION of ~1.2842 and printed pooled AUC 2.0075 instead of
        // the true ~0.7233 (2.0075 - 1.2842; matches the 5-fold mean AUC 0.7235).
        // Per-skill and fold calls (every one < 46,341 positives) were never
        // affected. The subtraction now runs in double, and an impossible result
        // throws instead of printing.
        double halfSumPos = (double)nPos * (nPos + 1) / 2.0;
        double auc = (rankSumPos - halfSumPos) / (nPos * (double)nNeg);
        const double AucEps = 1e-6;
        if (auc < -AucEps || auc > 1.0 + AucEps)
            throw new InvalidOperationException(string.Format(
                "ComputeAucRoc produced an impossible AUC ({0:F6}) with n={1}, n_pos={2}, n_neg={3} " +
                "- aborting the run rather than reporting an invalid metric.", auc, n, nPos, nNeg));
        return Math.Min(1.0, Math.Max(0.0, auc));
    }

    public static double ComputeRmse(IList<double> predictions, IList<int> outcomes)
    {
        int n = predictions.Count;
        if (n == 0) return double.NaN;
        double sumSq = 0.0;
        for (int i = 0; i < n; i++)
        {
            double d = predictions[i] - outcomes[i];
            sumSq += d * d;
        }
        return Math.Sqrt(sumSq / n);
    }

    public static double ComputeMae(IList<double> predictions, IList<int> outcomes)
    {
        int n = predictions.Count;
        if (n == 0) return double.NaN;
        double sumAbs = 0.0;
        for (int i = 0; i < n; i++)
            sumAbs += Math.Abs(predictions[i] - outcomes[i]);
        return sumAbs / n;
    }

    /// <summary>Accuracy at the 0.5 threshold (pyBKT convention).</summary>
    public static double ComputeAccuracy(IList<double> predictions, IList<int> outcomes, double threshold = 0.5)
    {
        int n = predictions.Count;
        if (n == 0) return double.NaN;
        int correct = 0;
        for (int i = 0; i < n; i++)
        {
            bool predictedPositive = predictions[i] >= threshold;
            if (predictedPositive == (outcomes[i] == 1)) correct++;
        }
        return (double)correct / n;
    }

    /// <summary>
    /// Deterministic student-level k-fold split (studies split by student so
    /// no learner's responses leak across train/test folds). Same student ID
    /// always lands in the same fold for a given (folds, seed) pair.
    /// </summary>
    public static Dictionary<string, int> AssignKFolds(IEnumerable<string> studentIds, int folds, int seed)
    {
        if (folds < 1) folds = 1;
        var ids = studentIds.Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();
        var rng = new System.Random(seed);
        // Fisher-Yates shuffle of the student list for fold assignment.
        for (int i = ids.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            // Explicit temp (not tuple-swap): some Unity/Mono compiler versions
            // miscompile `(a,b) = (b,a)` as sequential assignment without a temp,
            // which silently duplicates and drops student IDs from the fold map.
            string tmp = ids[i];
            ids[i] = ids[j];
            ids[j] = tmp;
        }
        var map = new Dictionary<string, int>(ids.Count);
        for (int i = 0; i < ids.Count; i++)
            map[ids[i]] = i % folds;
        return map;
    }

    /// <summary>Mean and standard deviation of a metric across folds.</summary>
    public static (double mean, double std) MeanStd(IList<double> values)
    {
        var clean = values.Where(v => !double.IsNaN(v)).ToList();
        if (clean.Count == 0) return (double.NaN, double.NaN);
        double mean = clean.Average();
        if (clean.Count == 1) return (mean, 0.0);
        double var = clean.Sum(v => (v - mean) * (v - mean)) / (clean.Count - 1);
        return (mean, Math.Sqrt(var));
    }

    /// <summary>
    /// Bootstrap 95% confidence interval by resampling STUDENTS with
    /// replacement (keeps each student's sequence intact, matching the
    /// unit of independence). Returns (low, high) percentiles.
    /// </summary>
    public static (double low, double high) BootstrapStudentCi(
        IList<(string studentId, double prediction, int outcome)> rows,
        Func<IList<double>, IList<int>, double> metric,
        int resamples, int seed)
    {
        var rng = new System.Random(seed);
        var byStudent = rows.GroupBy(r => r.studentId).ToDictionary(g => g.Key, g => g.ToList());
        var studentKeys = byStudent.Keys.ToList();
        if (studentKeys.Count == 0 || resamples < 1) return (double.NaN, double.NaN);

        var samples = new List<double>(resamples);
        for (int r = 0; r < resamples; r++)
        {
            var preds = new List<double>();
            var outs = new List<int>();
            for (int s = 0; s < studentKeys.Count; s++)
            {
                var pick = byStudent[studentKeys[rng.Next(studentKeys.Count)]];
                foreach (var row in pick)
                {
                    preds.Add(row.prediction);
                    outs.Add(row.outcome);
                }
            }
            samples.Add(metric(preds, outs));
        }
        samples.Sort();
        int loIdx = (int)(0.025 * (samples.Count - 1));
        int hiIdx = (int)(0.975 * (samples.Count - 1));
        return (samples[loIdx], samples[hiIdx]);
    }
}
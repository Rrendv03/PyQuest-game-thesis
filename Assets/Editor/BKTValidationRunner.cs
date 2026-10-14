#if UNITY_EDITOR
// =============================================================================
// BKTValidationRunner.cs  —  Unity Editor validation harness for the PyQuest
// BKT engine against published pyBKT benchmarks on ASSISTments 2009-2010.
//
// ARCHITECTURE (v2 — "connector only", agreed with the author 2026-10-07):
//   The engine is the object under test and is NEVER modified for this work.
//   There is no second BKT implementation in the evaluation path. The runner
//   drives the shipped BKTEngine component through exactly the public members
//   the game itself already uses (verified project-wide, 2026-10-07):
//
//     SanctumManager.cs 252/318 : BKTEngine.Instance.SaveUpdatedParameters();
//     MainMenuController.cs 219 : BKTEngine.Instance.ResetAllMastery();
//     PuzzleManager.cs 228      : BKTEngine.Instance.UpdateMastery(kc, ok, pGuessOverride);
//     EncounterManager.cs 807+  : GetMastery / UpdateMastery
//     PCGEngine.cs 108+         : GetMastery
//
//   How each validation need is met WITHOUT touching the engine:
//
//     1. Parameters from the study (P(L0)=0.08, learn=0.30, guess=0.15,
//        slip=0.05 — Badrinath, Wang & Pardos 2021 §4) reach the engine
//        THROUGH ITS OWN PRODUCTION LOADER: the runner writes a
//        components-schema bkt_params.json into Assets/StreamingAssets
//        (backing up the shipped file and restoring its exact bytes
//        afterwards), then starts the engine's own LoadParametersRoutine
//        coroutine and pumps it to completion. In Edit Mode Unity runs no
//        MonoBehaviour lifecycle, so the runner starts the same coroutine
//        Awake would start on device. Nothing here changes gameplay loading
//        in Play Mode or on Android.
//     2. Proof the parameters actually reached the PRIVATE engine state: the
//        runner calls the engine's own SaveUpdatedParameters() — the same
//        method SanctumManager calls at encounter end — and reads back the
//        JSON the engine serialized from its own dictionary. Every requested
//        skill must be present with the forced values (within float
//        round-trip tolerance), or the run aborts BEFORE any metric is
//        produced (no silent guessing).
//     3. Fresh prior for every replayed student: engine.ResetAllMastery()
//        (public, shipped; also used by MainMenuController).
//     4. Predicted P(correct) BEFORE each response = the standard-BKT
//        emission P(L)(1-P(S)) + (1-P(L))P(G), computed from the mastery the
//        engine itself is holding (engine.GetMastery) and the round-tripped
//        parameters (what the engine itself reports owning), via the
//        independently verified closed form (BKTEngineMath.PredictCorrect).
//        THE BAYESIAN UPDATE AND THE LEARNING TRANSITION — the model under
//        test — are always the engine's own UpdateMastery; the runner never
//        computes them.
//     5. Console quietness: the shipped engine logs one line per update; a
//        bulk replay wraps its loop in Debug.unityLogger.logEnabled = false
//        (official Unity API) and restores it in `finally`. Only the echo is
//        gated — the math is untouched.
//
//   Two parameter scenarios are reported, clearly labelled:
//     SCENARIO A — the study's parameter set (0.08/0.30/0.15/0.05): the
//                  citable published-benchmark comparison.
//     SCENARIO B — the shipped bkt_params.json values (synced to the
//                  workspace 2026-10-07): engine-representative, explicitly
//                  NOT benchmark-comparable, reported separately and labelled.
//
// WHAT THIS DOES *NOT* DO
//   - It does NOT run pyBKT. Published pyBKT metrics are cited as benchmarks
//     only (see the comparison block in the report and
//     BKT_Validation_Procedure.md §5).
//   - It does NOT fit parameters inside the game. The in-game engine stays
//     inference-only with fixed parameters (approved scope, doc 01).
//
// PLACEMENT: put this file under Assets/Editor/ in the Unity project so it
// compiles in Editor only. Menu: PyQuest > Validation > Run BKT Benchmark
// =============================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class BKTValidationRunner
{
    // -------------------------------------------------------------------------
    // CONFIGURATION — edit these to change a run; no other code changes needed.
    // -------------------------------------------------------------------------
    private const string CsvPath = "Assets/Data/as.csv";           // ASSISTments 2009-2010 (pyBKT-examples "as.csv")
    private const string ParamsFileName = "bkt_params.json";       // swapped per scenario, restored byte-exact afterwards
    private const string OutputDir = "Assets/BKT_Validation_Output";

    // Skills to evaluate: the two skills the pyBKT paper names in its
    // cross-validation example plus the ten highest-trial skills of the
    // pyBKT-examples per-skill standard-KT annex table (cited with their
    // numbers in §5.2 of BKT_Validation_Procedure.md). Published standard-KT
    // numbers exist for every skill here, so every comparison row is checkable.
    private static readonly string[] Skills =
    {
        "Circle Graph",
        "Box and Whisker",
        "Venn Diagram",
        "Percent Of",
        "Area Rectangle",
        "Addition and Subtraction Integers",
        "Equation Solving Two or Fewer Steps",
        "Volume Rectangular Prism",
        "Volume Cylinder",
        "Addition and Subtraction Fractions",
        "Conversion of Fraction Decimals Percents",
        "Multiplication and Division Integers"
    };

    // Preprocessing matching the published protocol (Khajah et al. 2016 as
    // described in the pyBKT-examples README): first attempts only, and
    // discard students with only one trial for a skill.
    private const bool FirstAttemptOnly = true;     // keep only original == 1 rows
    private const bool DiscardSingleTrialStudents = true;

    // k-fold cross-validation (the studies use 5 folds; Bulut et al. 2023 use 3).
    // NOTE: with FIXED parameters there is no training step, so folds report
    // metric stability across student subsets (mean ± SD), not train/test
    // generalisation. If offline fitting (doc 03) happens later, the same fold
    // map is what fitted params must be evaluated with.
    private const int Folds = 5;
    private const int Seed = 42;                    // seed used in the papers' examples
    private const int BootstrapResamples = 1000;    // 95% CIs via student-level bootstrap

    // The study's parameters (Badrinath et al. 2021 §4, "common values seen for
    // Algebra skills"), forced through the engine's own loader.
    private const double StudyPL0 = 0.08, StudyT = 0.30, StudyG = 0.15, StudyS = 0.05;

    /// <summary>Round-trip tolerance: Unity's JsonUtility re-serializes floats
    /// with limited digits, so read-back equality is asserted at this slack —
    /// far below the 4e-4 worst-case float parity of the doc-06 playtest replay.</summary>
    private const float RoundTripTolerance = 1e-5f;

    // -------------------------------------------------------------------------
    // MENU
    // -------------------------------------------------------------------------
    [MenuItem("PyQuest/Validation/Run BKT ASSISTments Benchmark")]
    public static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[BKT-Validation] Leave Play Mode first. Edit Mode keeps the game's persisted " +
                           "BKTEngine.Instance singleton untouched (the runner's temporary engine never " +
                           "runs Awake), so nothing here may race a play session.");
            return;
        }
        RunCore(CsvPath, OutputDir);
    }

    /// <summary>
    /// The whole validation, parameterised so the offline verification harness
    /// (tools/bkt_validation_check) can execute this exact code end-to-end
    /// against a synthetic dataset. In Unity the menu entry is the only caller.
    /// </summary>
    internal static void RunCore(string csvPath, string outputDir, string[] skillsOverride = null)
    {
        var log = new StringBuilder();
        var skills = skillsOverride ?? Skills;
        string paramsPath = Path.Combine(Application.streamingAssetsPath, ParamsFileName);
        byte[] originalParamsBytes = File.Exists(paramsPath) ? File.ReadAllBytes(paramsPath) : null;

        try
        {
            if (!File.Exists(csvPath))
            {
                Debug.LogError("[BKT-Validation] Dataset not found at '" + csvPath + "'. " +
                               "Download the ORIGINAL-version ASSISTments 2009-2010 Skill Builder file " +
                               "(same bytes as pyBKT-examples data/as.csv; see BKT_Validation_Procedure.md §3 " +
                               "for the exact portal links) and place it there.");
                return;
            }

            Directory.CreateDirectory(outputDir);

            // --- 1. Parse CSV (ASSISTments column layout) ----------------------------
            var rows = ParseAssistmentsCsv(csvPath, out var parseNotes);
            foreach (var note in parseNotes) log.AppendLine(note);
            log.AppendLine("Parsed " + rows.Count + " response rows from " + Path.GetFileName(csvPath));
            log.AppendLine();

            // --- 2. Filter + group into (student, skill) sequences -------------------
            var sequences = BuildSequences(rows, skills, FirstAttemptOnly, DiscardSingleTrialStudents, out var filterNotes);
            foreach (var note in filterNotes) log.AppendLine(note);

            if (sequences.Count == 0)
            {
                log.AppendLine("ERROR: no sequences survived filtering. Check skill names against the CSV's skill_name column.");
                Debug.Log(log.ToString());
                return;
            }

            // --- 3. Shipped-parameter values for SCENARIO B ---------------------------
            // Read BEFORE anything touches the StreamingAssets file. If the shipped
            // file is absent or unparsable, Scenario B is skipped with a clear note
            // (no silent fallback — every source in the report states what it is).
            BktParams? shipped = null;
            string shippedNote;
            if (originalParamsBytes == null)
            {
                shippedNote = "shipped bkt_params.json absent — Scenario B skipped (benchmark Scenario A unaffected)";
            }
            else if (TryParseFirstComponent(ReadText(originalParamsBytes), out BktParams sp, out int kcCount))
            {
                shipped = sp;
                shippedNote = "shipped bkt_params.json (components schema, " + kcCount + " knowledge components) " +
                              "— values as synced from the local machine 2026-10-07";
            }
            else
            {
                shippedNote = "shipped bkt_params.json could not be parsed as any supported schema — Scenario B skipped";
            }
            log.AppendLine("Scenario B parameter source: " + shippedNote);
            log.AppendLine();

            // --- 4. Run the two scenarios ---------------------------------------------
            var allScenarioRows = new List<(string scenarioTag, string skill, string studentId, double pred, int outcome, string paramSource)>();

            var studyComponents = skills.Select(s => new KnowledgeComponent
            {
                name = s,
                p_init = (float)StudyPL0,
                p_transit = (float)StudyT,
                p_guess = (float)StudyG,
                p_slip = (float)StudyS,
                mastery_threshold = 0.85f
            }).ToList();

            allScenarioRows.AddRange(RunScenario("A",
                "SCENARIO A — STUDY PARAMETERS (benchmark; Badrinath, Wang & Pardos 2021 §4: prior=0.08, learn=0.30, guess=0.15, slip=0.05, forget absent)",
                studyComponents, sequences, skills, log));

            if (shipped != null)
            {
                var shippedComponents = skills.Select(s => new KnowledgeComponent
                {
                    name = s,
                    p_init = shipped.Value.pInit,
                    p_transit = shipped.Value.pTransit,
                    p_guess = shipped.Value.pGuess,
                    p_slip = shipped.Value.pSlip,
                    mastery_threshold = 0.85f
                }).ToList();

                allScenarioRows.AddRange(RunScenario("B",
                    "SCENARIO B — SHIPPED ENGINE PARAMETERS (the shipped bkt_params.json values; engine-representative, NOT benchmark-comparable)",
                    shippedComponents, sequences, skills, log));
            }

            // --- 5. Persist report + predictions CSV ----------------------------------
            string reportPath = Path.Combine(outputDir, "bkt_validation_report.txt");
            File.WriteAllText(reportPath, log.ToString());
            WritePredictionsCsv(outputDir, allScenarioRows);
            Debug.Log("[BKT-Validation] Done.\n\n" + log.ToString() + "\nReport: " + reportPath);
        }
        catch (Exception ex)
        {
            Debug.LogError("[BKT-Validation] Failed: " + ex.Message + "\n" + ex.StackTrace);
        }
        finally
        {
            // Leave the project exactly as we found it: the shipped parameters file
            // byte-identical (or removed if it never existed), gameplay logging on.
            // (Each scenario also restores on its own; this is the belt-and-braces.)
            if (originalParamsBytes != null) File.WriteAllBytes(paramsPath, originalParamsBytes);
            else if (File.Exists(paramsPath)) File.Delete(paramsPath);
            Debug.unityLogger.logEnabled = true;
        }
    }

    // -------------------------------------------------------------------------
    // ONE SCENARIO — the full connector flow, start to finish (one engine).
    // Owns its engine's whole lifecycle: temporary probe object, boot through
    // the engine's own loader, round-trip verification, replay, metrics — and
    // restores the StreamingAssets parameter file even on failure.
    // -------------------------------------------------------------------------
    private static List<(string scenarioTag, string skill, string studentId, double pred, int outcome, string paramSource)>
        RunScenario(string tag, string label, List<KnowledgeComponent> components,
                    List<SkillSequence> sequences, string[] orderedSkills, StringBuilder log)
    {
        string paramsPath = Path.Combine(Application.streamingAssetsPath, ParamsFileName);
        byte[] originalBytes = File.Exists(paramsPath) ? File.ReadAllBytes(paramsPath) : null;
        GameObject probeGo = null;

        try
        {
            log.AppendLine();
            log.AppendLine("=======================================================================");
            log.AppendLine(label);
            log.AppendLine("=======================================================================");

            // ---- a. Force the parameters through the engine's OWN loader -----------
            Directory.CreateDirectory(Application.streamingAssetsPath);
            File.WriteAllText(paramsPath, BuildComponentsJson(components));

            probeGo = new GameObject("BKT_Validation_Probe");
            probeGo.hideFlags = HideFlags.HideAndDontSave;
            var engine = probeGo.AddComponent<BKTEngine>();      // Edit Mode: Awake does NOT run
            StartEngineLoader(engine);

            log.AppendLine("Parameters source: " + label);
            log.AppendLine("  P(L0)=" + components[0].p_init.ToString("F4", CultureInfo.InvariantCulture) +
                           "  P(T)=" + components[0].p_transit.ToString("F4", CultureInfo.InvariantCulture) +
                           "  P(G)=" + components[0].p_guess.ToString("F4", CultureInfo.InvariantCulture) +
                           "  P(S)=" + components[0].p_slip.ToString("F4", CultureInfo.InvariantCulture));

            if (!engine.IsLoaded)
                throw new Exception("The engine's own loader did not finish — no metric can be trusted. " +
                                    "Check the StreamingAssets folder and the bkt_params.json schema.");

            // ---- b. Round-trip proof via the engine's own serializer ----------------
            // SaveUpdatedParameters() is the exact persistence the game invokes at
            // encounter end (SanctumManager 252/318). The runner reads back the JSON
            // the ENGINE serialized from its own private dictionary: every requested
            // skill must be present with the forced values, or the run aborts BEFORE
            // any metric is produced. Called here legally — not mid-replay — and
            // only against the temporary swapped file, restored in `finally`.
            engine.SaveUpdatedParameters();
            var roundTrip = ParseComponents(File.ReadAllText(paramsPath));
            var missing = components.Where(c => !roundTrip.Any(r => r.name == c.name)).ToList();
            if (missing.Count > 0)
                throw new Exception("Round-trip verification failed: the engine's own serialized parameters do not " +
                                    "contain the requested skill(s): " + string.Join(", ", missing.Select(m => "'" + m.name + "'")) +
                                    ". The forced parameters did not reach the engine; aborting before scoring.");
            var valueErrors = new List<string>();
            foreach (var c in components)
            {
                var r = roundTrip.First(x => x.name == c.name);
                if (Math.Abs(r.p_init - c.p_init) > RoundTripTolerance || Math.Abs(r.p_transit - c.p_transit) > RoundTripTolerance ||
                    Math.Abs(r.p_guess - c.p_guess) > RoundTripTolerance || Math.Abs(r.p_slip - c.p_slip) > RoundTripTolerance)
                    valueErrors.Add("'" + c.name + "' requested (init " + F4(c.p_init) + ", transit " + F4(c.p_transit) +
                                    ", guess " + F4(c.p_guess) + ", slip " + F4(c.p_slip) + ") round-tripped (init " +
                                    F4(r.p_init) + ", transit " + F4(r.p_transit) + ", guess " + F4(r.p_guess) + ", slip " + F4(r.p_slip) + ")");
            }
            if (valueErrors.Count > 0)
                throw new Exception("Round-trip parameter VALUES disagree with what was forced: " +
                                    string.Join("; ", valueErrors) + " — aborting before scoring.");
            log.AppendLine("  Round-trip proof: SaveUpdatedParameters() — the engine's own serializer: all " +
                           components.Count + " components exact (tol 1e-5); the engine itself holds these values.");
            log.AppendLine("Engine under test: BKTEngine (shipped MonoBehaviour) — its own public API only; no re-implemented BKT math in the evaluation path.");
            AppendEngineProvenance(log);
            log.AppendLine();

            // ---- c. Replay the dataset ----------------------------------------------
            var scenarioRows = new List<(string scenarioTag, string skill, string studentId, double pred, int outcome, string paramSource)>();
            double maxParityDelta = 0.0;
            string paramTag = ParamTag(components);
            var skillCounts = new List<(string skill, int students, int responses, int positives)>();

            Debug.unityLogger.logEnabled = false;   // gates ONLY the per-response echo; the math is untouched
            try
            {
                foreach (var skill in orderedSkills)
                {
                    var skillSeqs = sequences.Where(s => s.skill == skill).ToList();
                    if (skillSeqs.Count == 0)
                    {
                        log.AppendLine("[WARN] Skill '" + skill + "' has no sequences after filtering — skipped.");
                        continue;
                    }

                    // The slip/guess/transit values used for scoring and parity are the
                    // ROUND-TRIPPED ones — exactly what the engine itself reports owning.
                    var rc = roundTrip.First(x => x.name == skill);
                    float transit = rc.p_transit, slip = rc.p_slip, guess = rc.p_guess;
                    int responses = 0, positives = 0;

                    foreach (var seq in skillSeqs)
                    {
                        engine.ResetAllMastery();       // sequence starts at p_init, as in the studies

                        foreach (var ev in seq.events)
                        {
                            int correct = ev.correct;
                            float pLBefore = engine.GetMastery(skill);                        // the engine's OWN state
                            double pred = BKTEngineMath.PredictCorrect(pLBefore, slip, guess); // BEFORE the update
                            scenarioRows.Add((tag, skill, seq.studentId, pred, correct, paramTag));
                            responses++;
                            if (correct == 1) positives++;

                            engine.UpdateMastery(skill, correct == 1);   // the real update; null override => static p_guess

                            // Independent cross-check: closed-form standard-BKT equations
                            // (BKTEngineMath — harness-verified separately against hand
                            // calculations). The engine's post-update state is read back
                            // through ITS OWN GetMastery, so the comparison is engine-value
                            // vs textbook-value, not mirror-self-agreement.
                            double gap = Math.Abs(
                                BKTEngineMath.UpdateMastery(pLBefore, correct == 1, transit, guess, slip) -
                                engine.GetMastery(skill));
                            if (gap > maxParityDelta) maxParityDelta = gap;
                        }
                    }

                    skillCounts.Add((skill, skillSeqs.Count, responses, positives));
                }
            }
            finally
            {
                Debug.unityLogger.logEnabled = true;   // gameplay logging on again, always
            }

            log.AppendLine("Cross-check vs the closed-form recurrence (BKTEngineMath): max |delta| = " +
                           maxParityDelta.ToString("F6", CultureInfo.InvariantCulture));
            // A float32 disagreement would appear at ~1e-7 at the smallest; 1e-9 is the
            // practical "exactly zero" test for float32 outputs of the same expressions.
            log.AppendLine(maxParityDelta <= 1e-9
                ? "  PASS — engine == textbook equations on every response."
                : "  FAIL — engine and textbook equations disagree; metrics withheld. Inspect UpdateMastery.");
            if (maxParityDelta > 1e-9)
                Debug.LogError("[BKT-Validation] Engine/recurrence parity FAILED (max |delta| = " + maxParityDelta + "). No metrics are published this run.");
            log.AppendLine();

            AppendMetricsSection(log, tag, scenarioRows, skillCounts);
            return scenarioRows;
        }
        finally
        {
            // The shipped bytes go back no matter what happened inside the scenario,
            // and the temporary scene object is never left behind.
            if (originalBytes != null) File.WriteAllBytes(paramsPath, originalBytes);
            else if (File.Exists(paramsPath)) File.Delete(paramsPath);
            if (probeGo != null) UnityEngine.Object.DestroyImmediate(probeGo);
        }
    }

    private static string F4(float v) => v.ToString("F4", CultureInfo.InvariantCulture);
    private static string ParamTag(List<KnowledgeComponent> components)
        => F2(components[0].p_init) + "/" + F2(components[0].p_transit) + "/" +
           F2(components[0].p_guess) + "/" + F2(components[0].p_slip);
    private static string F2(float v) => v.ToString("F2", CultureInfo.InvariantCulture);

    // -------------------------------------------------------------------------
    // METRICS — measurement only (AUC-ROC, RMSE, MAE, accuracy, folds, CIs)
    // -------------------------------------------------------------------------
    private static void AppendMetricsSection(StringBuilder log, string scenarioTag,
        List<(string scenarioTag, string skill, string studentId, double pred, int outcome, string paramSource)> scenarioRows,
        List<(string skill, int students, int responses, int positives)> skillCounts)
    {
        // Build with an explicit loop: Mono/mcs drops element names from
        // lambda-inferred tuples (see the comment in BuildSequences), so every
        // tuple variable that has member access must be a DECLARED named type.
        var rows = new List<(string skill, string studentId, double pred, int outcome)>();
        foreach (var r in scenarioRows) rows.Add((r.skill, r.studentId, r.pred, r.outcome));

        log.AppendLine("--- Metrics — scenario " + scenarioTag + " — accuracy = pre-response P(correct) >= 0.5 matched (NOT the gameplay mastery gate) ---");
        log.AppendLine("  skill".PadRight(43) + "stud".PadLeft(6) + "resp".PadLeft(7) + "corr%".PadLeft(7) +
                       "AUC".PadLeft(9) + "RMSE".PadLeft(9) + "MAE".PadLeft(9) + "ACC".PadLeft(9));
        foreach (var c in skillCounts)
        {
            var sr = rows.Where(r => r.skill == c.skill).ToList();
            if (sr.Count == 0) continue;
            var sp = sr.Select(x => x.pred).ToList();
            var so = sr.Select(x => x.outcome).ToList();
            log.AppendLine("  " + c.skill.PadRight(41) + c.students.ToString(CultureInfo.InvariantCulture).PadLeft(6) +
                           c.responses.ToString(CultureInfo.InvariantCulture).PadLeft(7) +
                           (100.0 * c.positives / c.responses).ToString("F1", CultureInfo.InvariantCulture).PadLeft(7) +
                           N4(BKTMetrics.ComputeAucRoc(sp, so)).PadLeft(9) + N4(BKTMetrics.ComputeRmse(sp, so)).PadLeft(9) +
                           N4(BKTMetrics.ComputeMae(sp, so)).PadLeft(9) + N4(BKTMetrics.ComputeAccuracy(sp, so)).PadLeft(9));
        }

        // Pooled: full detail line + the two CI lines the studies' protocol carries.
        var pp = rows.Select(r => r.pred).ToList();
        var po = rows.Select(r => r.outcome).ToList();
        double pooledAuc = BKTMetrics.ComputeAucRoc(pp, po);
        double pooledRmse = BKTMetrics.ComputeRmse(pp, po);
        var ciAuc = BKTMetrics.BootstrapStudentCi(rows.Select(r => (r.studentId, r.pred, r.outcome)).ToList(),
            BKTMetrics.ComputeAucRoc, BootstrapResamples, Seed);
        var ciRmse = BKTMetrics.BootstrapStudentCi(rows.Select(r => (r.studentId, r.pred, r.outcome)).ToList(),
            BKTMetrics.ComputeRmse, BootstrapResamples, Seed);
        log.AppendLine("  POOLED (all listed skills)".PadRight(43) +
                       rows.Select(r => r.studentId).Distinct().Count().ToString(CultureInfo.InvariantCulture).PadLeft(6) +
                       rows.Count.ToString(CultureInfo.InvariantCulture).PadLeft(7) +
                       (rows.Count > 0 ? 100.0 * po.Count(o => o == 1) / rows.Count : 0).ToString("F1", CultureInfo.InvariantCulture).PadLeft(7) +
                       N4(pooledAuc).PadLeft(9) + N4(pooledRmse).PadLeft(9) +
                       N4(BKTMetrics.ComputeMae(pp, po)).PadLeft(9) + N4(BKTMetrics.ComputeAccuracy(pp, po)).PadLeft(9));
        log.AppendLine("  AUC-ROC = " + N4(pooledAuc) + "  (95% CI " + N4(ciAuc.low) + "-" + N4(ciAuc.high) + ")");
        log.AppendLine("  RMSE    = " + N4(pooledRmse) + "  (95% CI " + N4(ciRmse.low) + "-" + N4(ciRmse.high) + ")");
        log.AppendLine();

        var foldMap = BKTMetrics.AssignKFolds(rows.Select(r => r.studentId), Folds, Seed);
        var foldAucs = new List<double>();
        for (int f = 0; f < Folds; f++)
        {
            var foldRows = rows.Where(r => foldMap[r.studentId] == f).ToList();
            if (foldRows.Count == 0) continue;
            foldAucs.Add(BKTMetrics.ComputeAucRoc(foldRows.Select(r => r.pred).ToList(), foldRows.Select(r => r.outcome).ToList()));
        }
        var foldStats = BKTMetrics.MeanStd(foldAucs);
        log.AppendLine("  5-fold AUC (student split, seed " + Seed + "; fixed params => stability across student subsets, not generalisation): " +
                       string.Join("  ", foldAucs.Select((v, i) => "f" + i + " " + N4(v))) +
                       "  (mean " + N4(foldStats.mean) + " +/- " + N4(foldStats.std) + ")");
        log.AppendLine();

        // --- Published benchmark comparison (CITED, not re-run) ---------------------
        // For the ten annex skills (which this run replayed), place the cited pyBKT
        // number right next to ours — the table the thesis comparison fills from.
        log.AppendLine("=== Published pyBKT benchmarks (cited — NOT re-run in this tool) ===");
        log.AppendLine("  Source: Badrinath, A., Wang, F., Pardos, Z.A. (2021), EDM, pp. 468-474 + pyBKT-examples annex (KtIdem.py, 5-fold CV, our file). Headline: standard BKT AUC 0.76 (Khajah 0.73; BKT+Forgets 0.83).");
        log.AppendLine("  Reading rule: published = EM-FITTED per-skill params; ours = fixed params — differences = fitting, not code (parity above; full context: BKT_Validation_Procedure.md §4.3/§11).");
        log.AppendLine("  skill                                 ours: AUC / RMSE      published (EM-fitted): AUC / RMSE");
        foreach (var skill in Skills.Where(Published.KtAnnex.ContainsKey))
        {
            string ours = "not replayed";
            var skillRows = rows.Where(r => r.skill == skill).ToList();
            if (skillRows.Count > 0)
            {
                double ourAuc = BKTMetrics.ComputeAucRoc(skillRows.Select(r => r.pred).ToList(), skillRows.Select(r => r.outcome).ToList());
                double ourRmse = BKTMetrics.ComputeRmse(skillRows.Select(r => r.pred).ToList(), skillRows.Select(r => r.outcome).ToList());
                ours = N3(ourAuc) + " / " + N3(ourRmse);
            }
            var pub = Published.KtAnnex[skill];
            log.AppendLine("  " + skill.PadRight(37) + " " + ours.PadRight(22) + " " + pub.auc.ToString("F3", CultureInfo.InvariantCulture) +
                           " / " + pub.rmse.ToString("F3", CultureInfo.InvariantCulture));
        }
        log.AppendLine();
        log.AppendLine("  Circle Graph / Box and Whisker published §3.1 values are the KT-IDEM VARIANT (MAE 0.41565/0.33906; AUC 0.72782/0.67991) — never compare as standard.");
        log.AppendLine();
        log.AppendLine("  Supporting-citation context: Bulut et al. 2023 (Psych; Cognitive Tutor — not ASSISTments), Gorgun & Bulut 2022, Shimada & Okada 2025 — procedure doc §2.4–2.6 (never numeric benchmarks here).");
        log.AppendLine("  Per-response predictions: bkt_validation_predictions.csv (scenario column). Full documentation: BKT_Validation_Procedure.md v4.3 (§11 = run-verification record).");
    }

    private static string N4(double v) => v.ToString("F4", CultureInfo.InvariantCulture);
    private static string N3(double v) => v.ToString("F3", CultureInfo.InvariantCulture);

    private static void AppendMetricBlock(StringBuilder log,
        IList<(string skill, string studentId, double pred, int outcome)> rows, int seed, int boot)
    {
        var preds = rows.Select(r => r.pred).ToList();
        var outs = rows.Select(r => r.outcome).ToList();
        double auc = BKTMetrics.ComputeAucRoc(preds, outs);
        double rmse = BKTMetrics.ComputeRmse(preds, outs);
        double mae = BKTMetrics.ComputeMae(preds, outs);
        double acc = BKTMetrics.ComputeAccuracy(preds, outs);

        var ciAuc = BKTMetrics.BootstrapStudentCi(rows.Select(r => (r.studentId, r.pred, r.outcome)).ToList(),
            BKTMetrics.ComputeAucRoc, boot, seed);
        var ciRmse = BKTMetrics.BootstrapStudentCi(rows.Select(r => (r.studentId, r.pred, r.outcome)).ToList(),
            BKTMetrics.ComputeRmse, boot, seed);

        log.AppendLine("  AUC-ROC = " + N4(auc) +
                       "  (95% CI " + N4(ciAuc.low) + "-" + N4(ciAuc.high) + ")");
        log.AppendLine("  RMSE    = " + N4(rmse) +
                       "  (95% CI " + N4(ciRmse.low) + "-" + N4(ciRmse.high) + ")");
        log.AppendLine("  MAE     = " + N4(mae));
        log.AppendLine("  Accuracy= " + N4(acc) + "  (threshold 0.5)");
    }

    // -------------------------------------------------------------------------
    // PER-SKILL PUBLISHED STANDARD-KT ANNEX (cited from the pyBKT-examples repo;
    // verified against the repo sources and BKT_Validation_Procedure.md §5.2).
    // -------------------------------------------------------------------------
    private static class Published
    {
        public struct KtValue { public double auc; public double rmse; }
        public static readonly Dictionary<string, KtValue> KtAnnex = new Dictionary<string, KtValue>
        {
            { "Percent Of",                               new KtValue { auc = 0.886, rmse = 0.339 } },
            { "Addition and Subtraction Integers",        new KtValue { auc = 0.785, rmse = 0.405 } },
            { "Conversion of Fraction Decimals Percents", new KtValue { auc = 0.648, rmse = 0.460 } },
            { "Volume Rectangular Prism",                 new KtValue { auc = 0.973, rmse = 0.133 } },
            { "Venn Diagram",                             new KtValue { auc = 0.897, rmse = 0.309 } },
            { "Equation Solving Two or Fewer Steps",      new KtValue { auc = 0.612, rmse = 0.465 } },
            { "Volume Cylinder",                          new KtValue { auc = 0.967, rmse = 0.214 } },
            { "Multiplication and Division Integers",     new KtValue { auc = 0.650, rmse = 0.387 } },
            { "Area Rectangle",                           new KtValue { auc = 0.987, rmse = 0.100 } },
            { "Addition and Subtraction Fractions",       new KtValue { auc = 0.677, rmse = 0.442 } }
        };
    }

    // -------------------------------------------------------------------------
    // ENGINE PROVENANCE — recorded in the report so the thesis can state exactly
    // which shipped source file produced these numbers.
    // -------------------------------------------------------------------------
    private static void AppendEngineProvenance(StringBuilder log)
    {
        string path = null;
        foreach (var candidate in new[] { "Assets/BKTEngine.cs", "BKTEngine.cs", "../BKTEngine.cs" })
        {
            if (File.Exists(candidate)) { path = Path.GetFullPath(candidate); break; }
        }

        if (path == null)
        {
            // Real Unity projects keep the engine in a source subfolder, so search
            // the whole Assets tree (Assets only - never Library/ProjectSettings).
            try
            {
                var found = Directory.GetFiles("Assets", "BKTEngine.cs", SearchOption.AllDirectories);
                if (found.Length > 0) path = Path.GetFullPath(found[0]);
            }
            catch (Exception) { /* leave path null -> manual-record fallback */ }
        }

        if (path == null)
        {
            log.AppendLine("  Source file: (BKTEngine.cs not found anywhere under Assets - record it manually)");
            return;
        }

        using (var sha = SHA256.Create())
        using (var stream = File.OpenRead(path))
        {
            string hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            log.AppendLine("  Source file: " + path);
            log.AppendLine("  SHA-256 of BKTEngine.cs at validation time: " + hash);
        }
    }

    // -------------------------------------------------------------------------
    // PARAMETERS — components-schema helpers (the schema the ENGINE itself loads)
    // -------------------------------------------------------------------------
    internal struct BktParams { public float pInit, pTransit, pGuess, pSlip; }

    /// <summary>
    /// Builds the components-schema JSON the engine's own loader parses, with the
    /// exact shape of the shipped file: {"components":[{name, p_init, p_transit,
    /// p_guess, p_slip, mastery_threshold}]}.
    /// </summary>
    internal static string BuildComponentsJson(List<KnowledgeComponent> components)
    {
        var sb = new StringBuilder();
        sb.Append("{\n  \"components\": [\n");
        for (int i = 0; i < components.Count; i++)
        {
            var c = components[i];
            sb.Append("    { \"name\": \"").Append(c.name.Replace("\"", "\\\"")).Append("\", ");
            sb.Append("\"p_init\": ").Append(c.p_init.ToString("R", CultureInfo.InvariantCulture)).Append(", ");
            sb.Append("\"p_transit\": ").Append(c.p_transit.ToString("R", CultureInfo.InvariantCulture)).Append(", ");
            sb.Append("\"p_guess\": ").Append(c.p_guess.ToString("R", CultureInfo.InvariantCulture)).Append(", ");
            sb.Append("\"p_slip\": ").Append(c.p_slip.ToString("R", CultureInfo.InvariantCulture)).Append(", ");
            sb.Append("\"mastery_threshold\": ").Append(c.mastery_threshold.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(" }");
            if (i < components.Count - 1) sb.Append(",");
            sb.Append("\n");
        }
        sb.Append("  ]\n}");
        return sb.ToString();
    }

    internal static bool TryParseFirstComponent(string json, out BktParams p, out int kcCount)
    {
        p = default;
        kcCount = 0;
        var list = ParseComponents(json);
        if (list.Count == 0) return false;
        kcCount = list.Count;
        p = new BktParams { pInit = list[0].p_init, pTransit = list[0].p_transit, pGuess = list[0].p_guess, pSlip = list[0].p_slip };
        return true;
    }

    /// <summary>
    /// Parses a components-schema parameter file. Uses the GAME'S OWN
    /// KnowledgeComponent/BKTParamWrapper serialization classes first — the same
    /// deserialization path the engine loader takes — and falls back to a regex
    /// pass so the runner stays testable outside Unity.
    /// </summary>
    internal static List<KnowledgeComponent> ParseComponents(string json)
    {
        try
        {
            var wrapper = JsonUtility.FromJson<BKTParamWrapper>(json);
            if (wrapper != null && wrapper.components != null && wrapper.components.Count > 0)
                return wrapper.components;
        }
        catch (Exception)
        {
            // fall through to the regex pass
        }

        var list = new List<KnowledgeComponent>();
        int bodyStart = json.IndexOf('[');
        int bodyEnd = json.LastIndexOf(']');
        if (bodyStart < 0 || bodyEnd <= bodyStart) return list;
        string body = json.Substring(bodyStart + 1, bodyEnd - bodyStart - 1);
        foreach (System.Text.RegularExpressions.Match entry in System.Text.RegularExpressions.Regex.Matches(body, "\\{([^}]*)\\}"))
        {
            string o = entry.Groups[1].Value;
            string name = MatchStr(o, "name");
            if (name == null) continue;
            list.Add(new KnowledgeComponent
            {
                name = name,
                p_init = MatchNum(o, "p_init"),
                p_transit = MatchNum(o, "p_transit"),
                p_guess = MatchNum(o, "p_guess"),
                p_slip = MatchNum(o, "p_slip"),
                mastery_threshold = MatchNum(o, "mastery_threshold")
            });
        }
        return list;
    }

    private static string MatchStr(string obj, string key)
    {
        var m = System.Text.RegularExpressions.Regex.Match(obj, "\"" + key + "\"\\s*:\\s*\"([^\"]*)\"");
        return m.Success ? m.Groups[1].Value : null;
    }

    private static float MatchNum(string obj, string key)
    {
        var m = System.Text.RegularExpressions.Regex.Match(obj, "\"" + key + "\"\\s*:\\s*(-?[0-9]*\\.?[0-9]+(?:[eE][-+]?[0-9]+)?)");
        return m.Success && float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)
            ? v : 0f;
    }

    /// <summary>Decodes bytes as latin-1 (byte == code point) without needing a
    /// codepage provider — the public as.csv carries a few non-UTF-8 bytes and no
    /// default UTF-8 read is guaranteed not to throw on them. The default
    /// evaluation skills are pure ASCII, so nothing in the benchmark shifts.</summary>
    private static string ReadText(byte[] bytes)
    {
        var chars = new char[bytes.Length];
        for (int i = 0; i < bytes.Length; i++) chars[i] = (char)bytes[i];
        return new string(chars);
    }

    // -------------------------------------------------------------------------
    // CSV PARSING (ASSISTments 2009-2010 "as.csv" from pyBKT-examples)
    // Columns present: order_id, assignment_id, user_id, assistment_id, problem_id,
    // original, correct, attempt_count, ..., skill_name, ..., template_id, ...
    // -------------------------------------------------------------------------
    private struct ResponseRow
    {
        public string studentId;
        public string skill;
        public int correct;      // 0/1
        public int original;     // 1 = first attempt on the problem
        public long order;       // order_id — sequence order within a student
    }

    private static List<ResponseRow> ParseAssistmentsCsv(string path, out List<string> notes)
    {
        notes = new List<string>();
        var rows = new List<ResponseRow>();
        int studentCol = -1, skillCol = -1, correctCol = -1, originalCol = -1, orderCol = -1;

        using (var reader = new StringReader(ReadText(File.ReadAllBytes(path))))
        {
            string headerLine = reader.ReadLine();
            if (headerLine == null) return rows;
            var headers = SplitCsvLine(headerLine);
            for (int i = 0; i < headers.Length; i++)
            {
                string h = headers[i].Trim().ToLowerInvariant();
                if (h == "user_id" || h == "anon student id" || h == "student_id") studentCol = i;
                else if (h == "skill_name" || h == "kc(default)" || h == "skill") skillCol = i;
                else if (h == "correct" || h == "correct first attempt" || h == "is_correct") correctCol = i;
                else if (h == "original") originalCol = i;
                else if (h == "order_id" || h == "order") orderCol = i;
            }
            if (studentCol < 0 || skillCol < 0 || correctCol < 0)
                throw new Exception("CSV is missing required columns (need user_id, skill_name, correct). " +
                                    "Found: " + string.Join(", ", headers));
            notes.Add("Column mapping: student='" + headers[studentCol] + "' skill='" + headers[skillCol] +
                      "' correct='" + headers[correctCol] + "' original=" +
                      (originalCol >= 0 ? "'" + headers[originalCol] + "'" : "MISSING") + " order='" +
                      (orderCol >= 0 ? headers[orderCol] : "MISSING") + "'");

            string line; long lineNo = 1;
            while ((line = reader.ReadLine()) != null)
            {
                lineNo++;
                if (string.IsNullOrWhiteSpace(line)) continue;
                var cols = SplitCsvLine(line);
                if (cols.Length <= Math.Max(studentCol, Math.Max(skillCol, correctCol))) continue;

                if (!TryParseCorrect(cols[correctCol], out int correct)) continue; // skips non-binary rows
                rows.Add(new ResponseRow
                {
                    studentId = cols[studentCol].Trim(),
                    skill = cols[skillCol].Trim(),
                    correct = correct,
                    original = originalCol >= 0 && cols.Length > originalCol && TryParseCorrect(cols[originalCol], out int o) ? o : 1,
                    order = orderCol >= 0 && cols.Length > orderCol && long.TryParse(cols[orderCol], out long ord) ? ord : lineNo
                });
            }
        }
        return rows;
    }

    private static bool TryParseCorrect(string s, out int value)
    {
        s = s.Trim();
        if (s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase)) { value = 1; return true; }
        if (s == "0" || s.Equals("false", StringComparison.OrdinalIgnoreCase)) { value = 0; return true; }
        value = -1; return false;
    }

    /// <summary>Quote-aware CSV line splitter (handles commas inside quotes).</summary>
    private static string[] SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes) { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        fields.Add(sb.ToString());
        return fields.ToArray();
    }

    // -------------------------------------------------------------------------
    // SEQUENCE BUILDING
    // -------------------------------------------------------------------------
    private struct SkillSequence
    {
        public string skill;
        public string studentId;
        public List<(int correct, long order)> events;
    }

    private static List<SkillSequence> BuildSequences(
        List<ResponseRow> rows, string[] skills, bool firstAttemptOnly, bool discardSingleTrial,
        out List<string> notes)
    {
        notes = new List<string>();
        var skillSet = new HashSet<string>(skills, StringComparer.OrdinalIgnoreCase);
        var filtered = rows.Where(r => skillSet.Contains(r.skill));
        if (firstAttemptOnly)
        {
            int before = filtered.Count();
            filtered = filtered.Where(r => r.original == 1);
            notes.Add("Preprocessing: first-attempt-only (original==1) kept " + filtered.Count() + "/" + before + " rows");
        }

        var grouped = filtered
            // Anonymous type rather than a tuple key: the toolchain this pipeline is also
            // compiled with for testing (Mono/mcs) drops element names from grouping-key
            // tuples, while an anonymous type behaves identically under Unity's Roslyn.
            .GroupBy(r => new { r.skill, r.studentId })
            .Select(g => new SkillSequence
            {
                skill = g.Key.skill,
                studentId = g.Key.studentId,
                events = g.OrderBy(r => r.order).Select(r => (r.correct, r.order)).ToList()
            })
            .ToList();

        if (discardSingleTrial)
        {
            int before = grouped.Count;
            grouped = grouped.Where(s => s.events.Count >= 2).ToList();
            notes.Add("Preprocessing: discarded single-trial (student,skill) sequences — " + grouped.Count + "/" + before + " kept " +
                      "(matches the Khajah et al. 2016 protocol as described in the pyBKT-examples README)");
        }
        notes.Add("Sequences built: " + grouped.Count + " (student, skill) pairs across " +
                  grouped.Select(s => s.skill).Distinct().Count() + " skills");
        return grouped;
    }

    // -------------------------------------------------------------------------
    // PREDICTIONS CSV — one row per scored response across both scenarios.
    // -------------------------------------------------------------------------
    private static void WritePredictionsCsv(string outputDir,
        List<(string scenarioTag, string skill, string studentId, double pred, int outcome, string paramSource)> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("scenario,skill,student_id,predicted_p_correct,actual_correct,parameters");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",",
                r.scenarioTag,
                r.skill.Contains(",") ? '"' + r.skill + '"' : r.skill,
                r.studentId.Contains(",") ? '"' + r.studentId + '"' : r.studentId,
                r.pred.ToString("F6", CultureInfo.InvariantCulture),
                r.outcome.ToString(CultureInfo.InvariantCulture),
                r.paramSource));
        File.WriteAllText(Path.Combine(outputDir, "bkt_validation_predictions.csv"), sb.ToString());
    }

    // -------------------------------------------------------------------------
    // STARTING THE ENGINE'S OWN LOADER IN EDIT MODE.
    // Unity gives Edit-Mode MonoBehaviours no lifecycle, so the runner starts the
    // exact coroutine the engine's Awake starts on device — via reflection,
    // because the method is private. No math, no state and no file outside the
    // parameters file is touched; in Play Mode / on device loading happens
    // automatically in Awake, so the runner does not interfere with the game's
    // real persisted singleton.
    // -------------------------------------------------------------------------
    internal static void StartEngineLoader(BKTEngine engine)
    {
        var method = typeof(BKTEngine).GetMethod("LoadParametersRoutine",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (method == null)
            throw new Exception("The engine's own loader (LoadParametersRoutine) was not found via reflection — " +
                                "did the shipped BKTEngine.cs change? Aborting before scoring.");
        var routine = (IEnumerator)method.Invoke(engine, null);
        if (routine == null)
            throw new Exception("The engine's loader did not start — aborting before scoring.");
        while (routine.MoveNext()) { /* pump to completion (one yield in the Editor branch) */ }
    }
}
#endif
// PyQuest — Phase A — NodeWalker.cs
// Typed direct-children enumeration for the IronPython AST (Node has no public
// ChildNodes accessor in 3.4.2; PythonWalker is depth-first and unsuitable for
// per-level children, so the supported node kinds are enumerated explicitly).
using System.Collections.Generic;
using System.Linq;
using IronPython.Compiler.Ast;

namespace PyQuest.Pcg.Ast
{
    public static class NodeWalk
    {
        public static List<Node> DirectChildren(Node node)
        {
            var outList = new List<Node>();
            if (node == null) return outList;

            if (node is PythonAst pa)
            {
                if (pa.Body != null) outList.Add(pa.Body);
                return outList;
            }
            if (node is SuiteStatement suite)
            {
                foreach (var s in suite.Statements) outList.Add(s);
                return outList;
            }
            if (node is IfStatement iff)
            {
                foreach (var t in iff.Tests) outList.Add(t);
                if (iff.ElseStatement != null) outList.Add(iff.ElseStatement);
                return outList;
            }
            if (node is IfStatementTest ift)
            {
                outList.Add(ift.Test);
                if (ift.Body != null) outList.Add(ift.Body);
                return outList;
            }
            if (node is ForStatement forr)
            {
                outList.Add(forr.Left); outList.Add(forr.List); if (forr.Body != null) outList.Add(forr.Body);
                return outList;
            }
            if (node is WhileStatement wh)
            {
                outList.Add(wh.Test); if (wh.Body != null) outList.Add(wh.Body);
                return outList;
            }
            if (node is AssignmentStatement assign)
            {
                foreach (var l in assign.Left) outList.Add(l);
                outList.Add(assign.Right);
                return outList;
            }
            if (node is AugmentedAssignStatement aug)
            {
                outList.Add(aug.Left); outList.Add(aug.Right);
                return outList;
            }
            if (node is ExpressionStatement es) { outList.Add(es.Expression); return outList; }
            if (node is CallExpression ce)
            {
                outList.Add(ce.Target);
                foreach (var a in ce.Args) outList.Add(a);
                return outList;
            }
            if (node is BinaryExpression be) { outList.Add(be.Left); outList.Add(be.Right); return outList; }
            if (node is UnaryExpression ue) { outList.Add(ue.Expression); return outList; }
            if (node is AndExpression an) { outList.Add(an.Left); outList.Add(an.Right); return outList; }
            if (node is OrExpression or) { outList.Add(or.Left); outList.Add(or.Right); return outList; }
            if (node is ConditionalExpression cond)
            {
                outList.Add(cond.Test); outList.Add(cond.TrueExpression); outList.Add(cond.FalseExpression);
                return outList;
            }
            if (node is ParenthesisExpression paren) { outList.Add(paren.Expression); return outList; }
            // NameExpression / ConstantExpression / empty / unlisted: leaf for the taught subset
            return outList;
        }
    }
}

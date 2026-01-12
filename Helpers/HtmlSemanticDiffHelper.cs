using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace SemanticDiff.Helpers
{
    public static class HtmlSemanticDiffHelper
    {
        private const string INS_CLASS = "diff-ins";
        private const string DEL_CLASS = "diff-del";
        private const string SPLIT_PATTERN = @"(\s+|[^\w\s])";

        public static string Diff(string oldHtml, string newHtml)
        {
            if (string.IsNullOrEmpty(oldHtml) && string.IsNullOrEmpty(newHtml)) return "";
            if (string.IsNullOrEmpty(newHtml)) return MarkupDeleted(oldHtml);
            if (string.IsNullOrEmpty(oldHtml)) return MarkupInserted(newHtml);

            var oldDoc = new HtmlDocument { OptionOutputAsXml = false }; oldDoc.LoadHtml(oldHtml);
            var newDoc = new HtmlDocument { OptionOutputAsXml = false }; newDoc.LoadHtml(newHtml);

            var oldTokens = Tokenize(oldDoc);
            var newTokens = Tokenize(newDoc);

            var oldMeaningful = oldTokens.Where(t => !string.IsNullOrWhiteSpace(t.Text)).ToList();
            var newMeaningful = newTokens.Where(t => !string.IsNullOrWhiteSpace(t.Text)).ToList();

            var diffs = ComputeDiff(
                oldMeaningful.Select(t => t.Text).ToList(),
                newMeaningful.Select(t => t.Text).ToList()
            );

            ApplyDiffToStructure(diffs, newTokens, oldMeaningful);

            return newDoc.DocumentNode.OuterHtml;
        }

        private static void ApplyDiffToStructure(List<DiffResult> diffs, List<Token> allNewTokens, List<Token> oldMeaningfulTokens)
        {
            var nodeUpdates = new Dictionary<HtmlNode, StringBuilder>();
            StringBuilder GetBuilder(HtmlNode node)
            {
                if (!nodeUpdates.ContainsKey(node)) nodeUpdates[node] = new StringBuilder();
                return nodeUpdates[node];
            }

            int diffIndex = 0;
            int oldMeaningfulIndex = 0;
            int newTokenIndex = 0;
            HtmlNode lastMeaningfulNode = allNewTokens.FirstOrDefault()?.Node;

            while (diffIndex < diffs.Count)
            {
                var diff = diffs[diffIndex];

                if (diff.Type == DiffType.Delete)
                {
                    var targetNode = (newTokenIndex < allNewTokens.Count)
                        ? allNewTokens[newTokenIndex].Node
                        : (lastMeaningfulNode ?? allNewTokens.LastOrDefault()?.Node);

                    if (targetNode != null)
                    {
                        var sb = GetBuilder(targetNode);
                        var delGroup = new StringBuilder();

                        while (diffIndex < diffs.Count && diffs[diffIndex].Type == DiffType.Delete)
                        {
                            if (delGroup.Length > 0) delGroup.Append(" ");
                            delGroup.Append(oldMeaningfulTokens[oldMeaningfulIndex].Text);
                            oldMeaningfulIndex++;
                            diffIndex++;
                        }

                        if (sb.Length > 0 && !char.IsWhiteSpace(sb[sb.Length - 1]))
                        {
                            sb.Append(" ");
                        }
                        else if (sb.Length == 0)
                        {
                            // If beginning of a node, proactively add space if the deleted text likely needs separation
                            // This fixes the case where a deletion is attached to a new text node immediately following an element like <strong>
                            if (delGroup.Length > 0 && char.IsLetterOrDigit(delGroup[0]))
                            {
                                sb.Append(" ");
                            }
                        }

                        sb.Append($"<del class='{DEL_CLASS}'>{SafeHtmlEncode(delGroup.ToString())}</del>");
                        
                        // Check if we need a trailing space to separate from next token
                        if (delGroup.Length > 0 && char.IsLetterOrDigit(delGroup[delGroup.Length - 1]) && newTokenIndex < allNewTokens.Count)
                        {
                            var nextText = allNewTokens[newTokenIndex].Text;
                            if (nextText.Length > 0 && char.IsLetterOrDigit(nextText[0]))
                            {
                                sb.Append(" ");
                            }
                        }
                    }
                    else
                    {
                        diffIndex++;
                    }
                    continue;
                }

                while (newTokenIndex < allNewTokens.Count && string.IsNullOrWhiteSpace(allNewTokens[newTokenIndex].Text))
                {
                    var token = allNewTokens[newTokenIndex];
                    GetBuilder(token.Node).Append(SafeHtmlEncode(token.Text));
                    if (token.Node != null) lastMeaningfulNode = token.Node;
                    newTokenIndex++;
                }

                if (newTokenIndex >= allNewTokens.Count)
                {
                    diffIndex++;
                    continue;
                }

                var currentToken = allNewTokens[newTokenIndex];
                if (currentToken.Node != null) lastMeaningfulNode = currentToken.Node;
                var sbCurrent = GetBuilder(currentToken.Node);

                if (diff.Type == DiffType.Equal)
                {
                    sbCurrent.Append(SafeHtmlEncode(currentToken.Text));
                    oldMeaningfulIndex++;
                    diffIndex++;
                    newTokenIndex++;
                }
                else if (diff.Type == DiffType.Insert)
                {
                    var insGroup = new StringBuilder();
                    insGroup.Append(currentToken.Text);
                    diffIndex++;
                    newTokenIndex++;

                    while (newTokenIndex < allNewTokens.Count)
                    {
                        var nextToken = allNewTokens[newTokenIndex];
                        if (nextToken.Node != currentToken.Node) break;

                        if (string.IsNullOrWhiteSpace(nextToken.Text))
                        {
                            insGroup.Append(nextToken.Text);
                            newTokenIndex++;
                        }
                        else if (diffIndex < diffs.Count && diffs[diffIndex].Type == DiffType.Insert)
                        {
                            insGroup.Append(nextToken.Text);
                            diffIndex++;
                            newTokenIndex++;
                        }
                        else break;
                    }
                    sbCurrent.Append($"<ins class='{INS_CLASS}'>{SafeHtmlEncode(insGroup.ToString())}</ins>");
                }
            }

            while (newTokenIndex < allNewTokens.Count)
            {
                var t = allNewTokens[newTokenIndex];
                GetBuilder(t.Node).Append(SafeHtmlEncode(t.Text));
                newTokenIndex++;
            }

            foreach (var kvp in nodeUpdates)
            {
                kvp.Key.InnerHtml = kvp.Value.ToString();
            }
        }

        private static List<Token> Tokenize(HtmlDocument doc)
        {
            var tokens = new List<Token>();
            var textNodes = doc.DocumentNode.SelectNodes("//text()[not(parent::script or parent::style)]");
            if (textNodes == null) return tokens;

            foreach (var node in textNodes)
            {
                string text = HttpUtility.HtmlDecode(node.InnerText);
                var parts = Regex.Split(text, SPLIT_PATTERN);

                foreach (var part in parts)
                {
                    if (part == "") continue;
                    tokens.Add(new Token { Text = part, Node = node });
                }
            }
            return tokens;
        }

        private static string SafeHtmlEncode(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var sb = new StringBuilder(text.Length);
            foreach (var c in text)
            {
                switch (c)
                {
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '&': sb.Append("&amp;"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        private static string MarkupDeleted(string html) => $"<del class='{DEL_CLASS}'>{SafeHtmlEncode(HttpUtility.HtmlDecode(html))}</del>";
        private static string MarkupInserted(string html) => $"<ins class='{INS_CLASS}'>{SafeHtmlEncode(HttpUtility.HtmlDecode(html))}</ins>";

        #region Myers Diff Logic
        private enum DiffType { Equal, Insert, Delete }
        private struct DiffResult { public DiffType Type; }

        private static List<DiffResult> ComputeDiff(List<string> oldList, List<string> newList)
        {
            int n = oldList.Count;
            int m = newList.Count;
            int[,] matrix = new int[n + 1, m + 1];

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    if (oldList[i - 1] == newList[j - 1])
                        matrix[i, j] = matrix[i - 1, j - 1] + 1;
                    else
                        matrix[i, j] = Math.Max(matrix[i - 1, j], matrix[i, j - 1]);
                }
            }
            var diffs = new List<DiffResult>();
            Backtrack(matrix, oldList, newList, n, m, diffs);
            return diffs;
        }

        private static void Backtrack(int[,] matrix, List<string> oldList, List<string> newList, int i, int j, List<DiffResult> diffs)
        {
            // Prefer earliest match in Old to keep deletions contiguous at end of block
            if (i > 0 && j > 0 && oldList[i - 1] == newList[j - 1] && matrix[i, j] > matrix[i - 1, j])
            {
                Backtrack(matrix, oldList, newList, i - 1, j - 1, diffs);
                diffs.Add(new DiffResult { Type = DiffType.Equal });
            }
            else if (j > 0 && (i == 0 || matrix[i, j - 1] >= matrix[i - 1, j]))
            {
                Backtrack(matrix, oldList, newList, i, j - 1, diffs);
                diffs.Add(new DiffResult { Type = DiffType.Insert });
            }
            else if (i > 0 && (j == 0 || matrix[i, j - 1] < matrix[i - 1, j]))
            {
                Backtrack(matrix, oldList, newList, i - 1, j, diffs);
                diffs.Add(new DiffResult { Type = DiffType.Delete });
            }
        }
        #endregion

        private class Token
        {
            public string Text { get; set; }
            public HtmlNode Node { get; set; }
        }
    }
}
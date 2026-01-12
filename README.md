# SemanticDiff: Intelligent HTML Diffing Helper

**SemanticDiff** is a specialized ASP.NET Core helper designed for enterprise-grade applications that need to compare two HTML documents accurately while **preserving the original DOM structure and formatting**.

Unlike standard text diff tools or simple `innerText` comparisons, SemanticDiff understands the web page structure. It maps differences back to the specific HTML nodes, ensuring that the resulting visual diff is not only accurate but also valid HTML that retains the original look and feel (styles, paragraphs, bolding, etc.).

## Why SemanticDiff?

When comparing HTML documents, developers often face two main problems:

1.  **Raw Text Diffing**: Treating HTML as a flat string results in "noisy" diffs where minor attribute changes or tag restructuring flag the entire section as different, often breaking the HTML rendering.
2.  **Inner Text Diffing**: Stripping tags to compare content solves the noise issue but **loses all formatting**. You can't reconstruct the bold text, headers, or complex layouts after the diff.

**The SemanticDiff Solution:**
SemanticDiff bridges this gap. It tokenizes the visible text content while keeping a reference to the **original DOM nodes**. After computing the semantic differences (insertions/deletions), it **maps these changes back into the original HTML structure**.

### Key Capabilities

- **Structure Preservation**: The output is the original HTML with standard `<ins>` and `<del>` tags injected precisely where changes occurred.
- **Format-Aware**: If a word inside a `<strong>` tag is changed, the tool marks specifically that word without breaking the bold styling.
- **Resilient to DOM Changes**: It focuses on content changes while respecting the document hierarchy.

## Enterprise Use Cases

This project acts as a reference implementation/helper for robust document handling in business software:

- **Contract Management**: When importing contracts or legal documents, you can automatically generate a "Redline" view showing exactly what clauses changed from the previous version without losing the legal document's formatting.
- **Change Tracking**: Ideal for audit logs where you need to visually show users what specifically changed in a rich-text field or CMS entry.
- **Document Versioning**: Compare historical versions of meaningful markup data (reports, policies) cleanly.

## How It Works

The core logic is encapsulated in `Helpers\HtmlSemanticDiffHelper.cs`:

1.  **Load & Parse**: Uses `HtmlAgilityPack` to load the HTML into a DOM.
2.  **Tokenize**: Extracts "meaningful" text tokens (words, punctuation) while linking them to their parent HTML nodes.
3.  **Diff**: Computes the Longest Common Subsequence (LCS) on the tokens.
4.  **Reconstruct**: Iterates through the diff operations and injects `<ins>` (for new tokens) and `<del>` (for old tokens) directly into the DOM nodes of the target document, handling edge cases like whitespace and node boundaries.

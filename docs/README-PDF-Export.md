# How to Export Architecture Documentation to PDF

This guide explains how to convert the `Architecture-Documentation.md` file into a professional PDF document that includes all the diagrams and formatting.

## Option 1: Using Pandoc (Recommended)

### Installation
```bash
# Windows (using Chocolatey)
choco install pandoc

# macOS (using Homebrew)
brew install pandoc

# Ubuntu/Debian
sudo apt-get install pandoc

# For LaTeX support (required for PDF)
# Windows: Install MiKTeX
# macOS: brew install --cask mactex
# Ubuntu: sudo apt-get install texlive-latex-base
```

### Convert to PDF
```bash
pandoc Architecture-Documentation.md -o BigCommerce-Migration-Architecture.pdf --pdf-engine=xelatex
```

### Convert to PDF with Better Formatting
```bash
pandoc Architecture-Documentation.md -o BigCommerce-Migration-Architecture.pdf \
  --pdf-engine=xelatex \
  --variable=geometry:margin=1in \
  --variable=fontsize:11pt \
  --variable=mainfont:"Arial" \
  --variable=monofont:"Courier New" \
  --toc \
  --number-sections \
  --highlight-style=tango
```

## Option 2: Using Typora (GUI Editor)

1. Download and install Typora from https://typora.io/
2. Open `Architecture-Documentation.md` in Typora
3. Go to File → Export → PDF
4. Choose your export options and save

## Option 3: Using Visual Studio Code

1. Install the "Markdown PDF" extension
2. Open `Architecture-Documentation.md` in VS Code
3. Right-click and select "Markdown PDF: Export (pdf)"
4. The PDF will be generated in the same directory

## Option 4: Using Online Converters

### Pandoc Try (Online)
1. Go to https://pandoc.org/try/
2. Copy and paste the markdown content
3. Set output format to PDF
4. Download the generated PDF

### Markdown to PDF (Online)
1. Go to https://md2pdf.netlify.app/
2. Upload the markdown file
3. Download the generated PDF

## Option 5: Using GitBook

1. Create a free account at https://gitbook.com/
2. Create a new space
3. Import the markdown file
4. Use GitBook's PDF export feature

## Option 6: Using Notion

1. Create a new Notion page
2. Copy and paste the markdown content
3. Use Notion's "Export as PDF" feature
4. Choose "Export" → "PDF" from the page menu

## Diagram Rendering Notes

**Important**: The Mermaid diagrams in the documentation are written in Mermaid syntax. Some converters may not render these diagrams automatically. For best results:

1. **Pandoc**: Use with mermaid-filter extension
2. **Typora**: Has built-in Mermaid support
3. **VS Code**: Install Mermaid preview extension

### Alternative: Convert Diagrams to Images First

If diagrams don't render properly, you can:

1. Use Mermaid Live Editor (https://mermaid.live/)
2. Paste each diagram code
3. Export as PNG/SVG
4. Replace the diagram code blocks with image references

## Formatting Tips for Better PDF

### Add Cover Page
Add this at the beginning of the markdown file:
```markdown
---
title: "BigCommerce Migration Architecture"
author: "Your Name"
date: "January 2024"
---

\newpage
```

### Add Page Breaks
Add `\newpage` where you want page breaks:
```markdown
## Section 1
Content here...

\newpage

## Section 2
Content here...
```

### Custom CSS for Web-based Converters
Create a `styles.css` file:
```css
body {
    font-family: Arial, sans-serif;
    font-size: 11pt;
    line-height: 1.6;
    color: #333;
    max-width: 8.5in;
    margin: 0 auto;
    padding: 1in;
}

h1, h2, h3 {
    color: #2c3e50;
    page-break-after: avoid;
}

pre {
    background-color: #f8f9fa;
    border: 1px solid #dee2e6;
    border-radius: 4px;
    padding: 1em;
    overflow-x: auto;
}

table {
    border-collapse: collapse;
    width: 100%;
    margin-bottom: 1em;
}

th, td {
    border: 1px solid #dee2e6;
    padding: 8px;
    text-align: left;
}

th {
    background-color: #f8f9fa;
    font-weight: bold;
}

.page-break {
    page-break-before: always;
}
```

## Recommended Tools by Use Case

- **Professional Documentation**: Pandoc with LaTeX
- **Quick Export**: Typora or VS Code extension
- **Team Collaboration**: GitBook or Notion
- **No Installation**: Online converters

## Final PDF Checklist

- [ ] All diagrams are visible and properly formatted
- [ ] Table of contents is generated
- [ ] Code blocks are properly formatted
- [ ] Page breaks are in appropriate locations
- [ ] Headers and footers are configured
- [ ] Font sizes are consistent and readable
- [ ] All hyperlinks work (if needed)
- [ ] Images/diagrams are high resolution

## Troubleshooting

### Common Issues

1. **Diagrams not rendering**: Use Typora or convert diagrams to images first
2. **LaTeX errors**: Install full LaTeX distribution (MiKTeX/MacTeX)
3. **Font issues**: Specify fonts that exist on your system
4. **Large file size**: Compress images or use different image formats

### Getting Help

- Pandoc documentation: https://pandoc.org/MANUAL.html
- Typora support: https://support.typora.io/
- Mermaid documentation: https://mermaid-js.github.io/mermaid/

---

Choose the method that best fits your needs and technical setup. For the most professional results with proper diagram rendering, Pandoc with LaTeX is recommended. 
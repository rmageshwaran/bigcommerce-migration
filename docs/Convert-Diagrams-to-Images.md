# Convert Mermaid Diagrams to Images

This guide explains how to convert the three Mermaid diagram files (`.mmd`) to image formats (PNG, SVG, or PDF) that can be embedded in documents.

## Diagram Files Created:
1. `diagram-1-complete-flow.mmd` - Complete migration flow diagram
2. `diagram-2-component-architecture.mmd` - Component architecture diagram  
3. `diagram-3-sequence-flow.mmd` - Sequence flow diagram

## Method 1: Using Mermaid CLI (Recommended)

### Installation
```bash
# Install Mermaid CLI globally
npm install -g @mermaid-js/mermaid-cli

# Or using yarn
yarn global add @mermaid-js/mermaid-cli
```

### Convert to PNG (High Quality)
```bash
# Convert all diagrams to PNG
mmdc -i diagram-1-complete-flow.mmd -o diagram-1-complete-flow.png -w 1920 -H 1080
mmdc -i diagram-2-component-architecture.mmd -o diagram-2-component-architecture.png -w 1920 -H 1080
mmdc -i diagram-3-sequence-flow.mmd -o diagram-3-sequence-flow.png -w 1920 -H 1080
```

### Convert to SVG (Vector Format)
```bash
# Convert all diagrams to SVG
mmdc -i diagram-1-complete-flow.mmd -o diagram-1-complete-flow.svg
mmdc -i diagram-2-component-architecture.mmd -o diagram-2-component-architecture.svg
mmdc -i diagram-3-sequence-flow.mmd -o diagram-3-sequence-flow.svg
```

### Convert to PDF
```bash
# Convert all diagrams to PDF
mmdc -i diagram-1-complete-flow.mmd -o diagram-1-complete-flow.pdf
mmdc -i diagram-2-component-architecture.mmd -o diagram-2-component-architecture.pdf
mmdc -i diagram-3-sequence-flow.mmd -o diagram-3-sequence-flow.pdf
```

### Batch Convert All Diagrams
```bash
# Windows PowerShell
Get-ChildItem -Filter "*.mmd" | ForEach-Object { mmdc -i $_.Name -o ($_.BaseName + ".png") -w 1920 -H 1080 }

# macOS/Linux
for file in *.mmd; do mmdc -i "$file" -o "${file%.mmd}.png" -w 1920 -H 1080; done
```

## Method 2: Using Mermaid Live Editor (Online)

### Steps:
1. Go to https://mermaid.live/
2. Copy the content from each `.mmd` file
3. Paste it into the editor
4. Click "Actions" → "Download PNG" or "Download SVG"
5. Save the image with appropriate filename

### Benefits:
- No installation required
- Instant preview
- Multiple export formats
- Easy to use

## Method 3: Using VS Code Extension

### Installation:
1. Install "Mermaid Markdown Syntax Highlighting" extension
2. Install "Mermaid Preview" extension

### Steps:
1. Open the `.mmd` file in VS Code
2. Right-click and select "Mermaid: Preview"
3. Right-click on the preview and "Save Image As..."

## Method 4: Using Typora

### Steps:
1. Open Typora
2. Create a new document
3. Insert the Mermaid diagram code in a code block with `mermaid` language
4. Right-click on the rendered diagram
5. Select "Copy Image" or "Save Image As..."

## Method 5: Using Kroki (Online API)

### Using curl:
```bash
# Convert to PNG
curl -X POST "https://kroki.io/mermaid/png" \
  -H "Content-Type: text/plain" \
  --data-binary @diagram-1-complete-flow.mmd \
  -o diagram-1-complete-flow.png

# Convert to SVG
curl -X POST "https://kroki.io/mermaid/svg" \
  -H "Content-Type: text/plain" \
  --data-binary @diagram-1-complete-flow.mmd \
  -o diagram-1-complete-flow.svg
```

## Method 6: Using Docker (Mermaid CLI)

### If you don't want to install Node.js:
```bash
# Convert using Docker
docker run --rm -v $(pwd):/data minlag/mermaid-cli -i /data/diagram-1-complete-flow.mmd -o /data/diagram-1-complete-flow.png -w 1920 -H 1080
```

## Image Quality Settings

### For High-Quality Images:
- **PNG**: Use `-w 1920 -H 1080` or higher
- **SVG**: Vector format, scales perfectly
- **PDF**: Good for printing and embedding

### For Web Usage:
- **PNG**: Use `-w 1200 -H 800`
- **SVG**: Best for web, smallest file size

## Batch Script for Windows

Create `convert-diagrams.bat`:
```batch
@echo off
echo Converting Mermaid diagrams to PNG...
mmdc -i diagram-1-complete-flow.mmd -o diagram-1-complete-flow.png -w 1920 -H 1080
mmdc -i diagram-2-component-architecture.mmd -o diagram-2-component-architecture.png -w 1920 -H 1080
mmdc -i diagram-3-sequence-flow.mmd -o diagram-3-sequence-flow.png -w 1920 -H 1080
echo Done! Check the PNG files in the same directory.
pause
```

## Bash Script for macOS/Linux

Create `convert-diagrams.sh`:
```bash
#!/bin/bash
echo "Converting Mermaid diagrams to PNG..."
mmdc -i diagram-1-complete-flow.mmd -o diagram-1-complete-flow.png -w 1920 -H 1080
mmdc -i diagram-2-component-architecture.mmd -o diagram-2-component-architecture.png -w 1920 -H 1080
mmdc -i diagram-3-sequence-flow.mmd -o diagram-3-sequence-flow.png -w 1920 -H 1080
echo "Done! Check the PNG files in the same directory."
```

Make it executable:
```bash
chmod +x convert-diagrams.sh
./convert-diagrams.sh
```

## Troubleshooting

### Common Issues:
1. **Node.js not installed**: Download from https://nodejs.org/
2. **Puppeteer errors**: Install Chrome/Chromium browser
3. **Memory issues**: Reduce image size or use SVG format
4. **Font issues**: Install standard fonts or use system fonts

### Performance Tips:
- Use SVG for scalable diagrams
- Use PNG for fixed-size images
- Batch convert all diagrams at once
- Use online tools for quick conversions

## Recommended Workflow:

1. **For Documentation**: Convert to PNG (1920x1080) and SVG
2. **For Web**: Use SVG format
3. **For Print**: Convert to PDF
4. **For Presentations**: Use PNG (1920x1080)

---

Choose the method that best fits your setup and requirements. The Mermaid CLI is recommended for batch processing and automation, while online tools are great for quick one-off conversions. 
# Phase: DocFX Logo Size Fix

## Problem
The navbar logo on the DocFX documentation site renders at its native image size (512x512), making it way too large.

## Fix (2 changes)

### 1. `docfx.json` — add custom template
Change the template list from:
```json
"template": ["default", "modern"]
```
to:
```json
"template": ["default", "modern", "template"]
```

### 2. Create `template/public/main.css`
```css
/* Constrain the navbar logo to a reasonable size */
.navbar-brand img#logo {
    height: 36px;
    width: auto;
}
```

## Status
Complete — custom template added to docfx.json, CSS constrains logo to 36px height.

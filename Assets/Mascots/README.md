# Mascot illustrations

Drop the finished mascot PNGs here. As soon as a file exists, the app shows it
instead of the built-in vector fallback (see `Views/MascotImageLoader.cs`).
No code change needed — just add the file and rebuild.

## Expected files

| File                | Mode           | Concept                        | Palette        |
|---------------------|----------------|--------------------------------|----------------|
| `guardi.png`        | Sub            | Guardi shield character        | sky blue       |
| `trusted-sub.png`   | Trusted Sub    | smiling school backpack        | teal / green   |
| `restricted-sub.png`| Restricted Sub | smiling padlock (no keyhole)   | amber / orange |

## Art spec

- **Format:** PNG, **transparent background** (alpha).
- **Size:** at least **512 px** on the longest side (e.g. 600×680). Bigger is fine —
  it's scaled down crisply. Don't ship anything below ~256 px.
- **Framing:** subject centred, with a little margin so it isn't cropped at small sizes
  (the mascot renders as small as ~44 px in the tray menu).
- **Aspect:** roughly portrait/square. Rendering uses `Stretch="Uniform"`, so the image
  is never distorted — any ratio is safe, it just letterboxes inside its slot.
- **Style:** keep the **three mascots consistent** with each other (same shading /
  line treatment), each in its mode palette above.

## Swapping / iterating

Replace the file and rebuild (or restart the app — images are loaded with
`IgnoreImageCache`, so a fresh copy is picked up on the next launch).

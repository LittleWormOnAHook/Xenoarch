"""Builds Xenoarch Sol PBR terrain textures from Dark Matter Genesis CC0 sources (read-only).
Output (2048^2): <Layer>_Albedo.jpg (sRGB), <Layer>_Normal.png (OpenGL), <Layer>_Mask.png (URP: R metallic 0, G AO, B height, A smoothness).
Albedo is re-graded from luminance onto a body palette (the Genesis copies were tinted toward Io colours)."""
import os, sys, json
import numpy as np
from PIL import Image, ImageFilter
Image.MAX_IMAGE_PIXELS = None
SRC = r"A:\Dark Matter Genesis\Assets\_Project\Art\Terrain\Terrain_HiRes_4K"
DST = r"C:\Users\Teabagger\xa_solpbr"
N = 2048
# palette stops (linear-ish sRGB 0-1): dark, mid, light
MARS_REG  = [(0.28,0.13,0.07),(0.62,0.33,0.18),(0.84,0.60,0.42)]
MARS_ROCK = [(0.16,0.08,0.05),(0.42,0.22,0.13),(0.66,0.44,0.31)]
MARS_CRUST= [(0.36,0.20,0.12),(0.64,0.42,0.28),(0.82,0.65,0.50)]
MARS_SOIL = [(0.17,0.07,0.04),(0.36,0.16,0.08),(0.54,0.29,0.17)]
MOON_REG  = [(0.17,0.17,0.17),(0.44,0.43,0.42),(0.70,0.69,0.67)]
MOON_ROCK = [(0.10,0.10,0.10),(0.33,0.33,0.32),(0.60,0.59,0.57)]
MOON_SOIL = [(0.08,0.08,0.08),(0.22,0.21,0.20),(0.38,0.37,0.35)]
JOBS = {
  "Mars_Regolith":     (r"Set05_Ash_Regolith\ACG_Ground099",       "Io4K_Set05_ACG_Ground099",    MARS_REG,  1.0),
  "Mars_Rock":         (r"Set01_Basalt_Sulfur_Dust\TC_Rock0005",   "Io4K_Set01_TC_Rock0005",      MARS_ROCK, 1.1),
  "Mars_CrackedCrust": (r"Set10_Anthropogenic_Trace\TC_Ground0046","Io4K_Set10_TC_Ground0046",    MARS_CRUST,1.0),
  "Mars_SoilPBR":      (r"Set02_Sulfur_Plains\TC_Ground0004",      "Io4K_Set02_TC_Ground0004",    MARS_SOIL, 1.0),
  "Moon_Regolith":     (r"Set05_Ash_Regolith\ACG_Ground099",       "Io4K_Set05_ACG_Ground099",    MOON_REG,  1.0),
  "Moon_Rock":         (r"Set01_Basalt_Sulfur_Dust\ACG_Rock030",   "Io4K_Set01_ACG_Rock030",      MOON_ROCK, 1.1),
  "Moon_SoilPBR":      (r"Set02_Sulfur_Plains\TC_Ground0004",      "Io4K_Set02_TC_Ground0004",    MOON_SOIL, 1.0),
}

FLATTEN = {"Mars_Regolith": 0.75, "Moon_Regolith": 0.75}  # remove cloudy low-frequency blotches (less visible tiling)

def grade(rgb, stops, contrast, flatten=0.0):
    a = rgb.astype(np.float32) / 255.0
    L = 0.2126*a[...,0] + 0.7152*a[...,1] + 0.0722*a[...,2]
    if flatten > 0:
        img = Image.fromarray((L * 255).astype(np.uint8))
        # wrap-around blur so the texture stays seamless
        tiled = Image.new("L", (img.width * 3, img.height * 3))
        for i in range(3):
            for j in range(3): tiled.paste(img, (i * img.width, j * img.height))
        low = np.asarray(tiled.filter(ImageFilter.GaussianBlur(img.width / 16)))[img.height:2*img.height, img.width:2*img.width].astype(np.float32) / 255
        L = L - flatten * (low - low.mean())
    lo, hi = np.percentile(L, 1), np.percentile(L, 99)
    t = np.clip((L - lo) / max(1e-4, hi - lo), 0, 1)
    t = np.clip(0.5 + (t - t.mean()) * contrast, 0, 1)
    d, m, l = [np.array(s, np.float32) for s in stops]
    t3 = t[..., None]
    out = np.where(t3 < 0.5, d + (m - d) * (t3 / 0.5), m + (l - m) * ((t3 - 0.5) / 0.5))
    return (np.clip(out, 0, 1) * 255 + 0.5).astype(np.uint8)

def main(only=None):
    report = {}
    for name, (folder, base, stops, contrast) in JOBS.items():
        if only and name not in only: continue
        src = os.path.join(SRC, folder)
        out = os.path.join(DST, name); os.makedirs(out, exist_ok=True)
        col = Image.open(os.path.join(src, base + "_Color.jpg")).convert("RGB").resize((N, N), Image.LANCZOS)
        Image.fromarray(grade(np.asarray(col), stops, contrast, FLATTEN.get(name, 0.0))).save(os.path.join(out, name + "_Albedo.jpg"), quality=93, subsampling=0)
        nrm = Image.open(os.path.join(src, base + "_Normal.png")).convert("RGB").resize((N, N), Image.LANCZOS)
        nrm.save(os.path.join(out, name + "_Normal.png"), optimize=True)
        mk = Image.open(os.path.join(src, base + "_MaskHDRP.png")).convert("RGBA").resize((N, N), Image.LANCZOS)
        mk = np.asarray(mk)
        h = np.asarray(Image.open(os.path.join(src, base + "_Height.png")))
        h = h.astype(np.float32); h = (h - h.min()) / max(1.0, h.max() - h.min())
        h = np.asarray(Image.fromarray((h * 255).astype(np.uint8)).resize((N, N), Image.LANCZOS))
        urp = np.zeros((N, N, 4), np.uint8)
        urp[..., 0] = 0                 # metallic
        urp[..., 1] = mk[..., 1]        # AO
        urp[..., 2] = h                 # height (height-based blend)
        urp[..., 3] = mk[..., 3]        # smoothness
        Image.fromarray(urp, "RGBA").save(os.path.join(out, name + "_Mask.png"), optimize=True)
        report[name] = {"source": folder, "smoothness_mean": float(mk[..., 3].mean() / 255), "ao_mean": float(mk[..., 1].mean() / 255)}
        print(name, "ok", report[name], flush=True)
    with open(os.path.join(DST, "build_report.json"), "w") as f: json.dump(report, f, indent=1)

if __name__ == "__main__":
    main(sys.argv[1:] or None)

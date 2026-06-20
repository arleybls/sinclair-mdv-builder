# MicroPicoDrive — Minerva Format Compatibility Notes

> Reverse-engineering notes on how `gusmanb/micropicodrive` makes QL Microdrive
> formatting work under the **Minerva** ROM, plus the underlying MDV / real-tape
> format details that explain *why* the firmware does what it does.
>
> Source examined: `gusmanb/micropicodrive` @ `master`. All line numbers refer to
> that tree. Treat them as a snapshot — re-check against your checked-out commit.

---

## TL;DR

- Minerva **rejects "perfect" microdrives**: a freshly formatted cartridge with
  zero bad sectors is refused with `Format failed`. This is real, not folklore —
  it's reproduced by users on the QL Forum and worked around in firmware.
- The current firmware **already handles this automatically**, but **only during a
  real format performed on the QL through the device** (`inFormat` gated).
- The workaround has two parts, both applied per-sector while `inFormat`:
  1. **Skip sector 254** → cartridge ends up with a non-full geometry.
  2. **Damage sector 13** → corrupt two bytes so that sector fails read-back
     verify, giving Minerva the "imperfect tape" it expects.
- **Gotcha:** importing a pristine software-made MDV (qlay2 / mdvtool, full 255
  good sectors, intact sector 13) does **not** trigger any of this → Minerva may
  reject it. For Minerva, **format through the device**, don't import a clean image.

---

## 1. The "perfect cartridge" problem

On a real Microdrive tape, some sectors always fail to verify after a format — the
medium is physically imperfect. Minerva's format routine appears to *rely* on that:
if every sector reads back perfectly, it treats the result as invalid and reports
`Format failed`. JM/JS ROMs are more tolerant.

An emulator that faithfully returns exactly what was written produces a flawless
cartridge every time → Minerva refuses it. MicroPicoDrive compounds this because it
explicitly **regenerates perfect bits** on playback (preamble + data are
reconstructed, ULA preamble "trash" is discarded), so there is no natural source of
read-back error.

Confirmed on the QL Forum:
- Thread **t=4523** (original MicroPicoDrive announcement): the author notes Minerva
  is picky with "perfect" microdrives and rejects a format unless some sectors are
  corrupt.
- Thread **t=4883** ("MicroPicoDrive Popopo's version"): a user running a Minerva
  ROM + 512 KB RAM expansion reports `Format mdv1_` → "Format failed", i.e. exactly
  this symptom on hardware.

---

## 2. Firmware mechanism (the fix that already exists)

File: **`Firmware/UserInterface.c`**

### 2.1 Format detection — the `inFormat` flag

| What | Where |
|------|-------|
| Declared | `bool inFormat = false;` (line ~142) |
| **Set** | line ~167 — when a header is processed and the written sector number is `255` (the marker the QL emits during a format pass), `inFormat = 1;` |
| **Cleared** | line ~285 — on drive deselect / end of operation, `inFormat = false;` |

So the accommodations below run **only during an actual format**, not during normal
reads/writes.

### 2.2 The two Minerva accommodations

Applied identically in both `process_md_read()` (~line 216) and
`process_md_write()` (~line 245). `secNum` is read from the stored image at
`cartridge_image[CARTRIDGE_SECTOR_SIZE * currentSector + 1]` (byte +1 of each sector
record = the sector-number byte).

**(a) Skip sector 254**
```c
if (inFormat && secNum == 254) {
    currentSector++;
    if (currentSector > 253)
        currentSector = 0;
}
```
Trims the geometry so the formatted cartridge is not a full, suspiciously-complete set.

**(b) Damage sector 13**
```c
if (inFormat && secNum == 13) {
    cartridge_image[CARTRIDGE_SECTOR_SIZE * currentSector + 13]  += 13;
    cartridge_image[CARTRIDGE_SECTOR_SIZE * currentSector + 128] += 13;
}
```
Two bytes are corrupted so that sector 13 fails read-back verify → Minerva sees the
expected bad sector and accepts the format.

> **Important side effect:** the damage is written into `cartridge_image` (the
> in-RAM image), not just the transmitted buffer. So once a format pass touches
> sector 13 the damage **persists**. If you then **save** the cartridge to SD, the
> Minerva-friendly bad sector is **baked into the saved `.mdv`** — which is
> desirable: that saved image is then Minerva-safe even if re-imported later.

### 2.3 Why TWO bytes, at +13 and +128?

This maps cleanly onto the real QL sector layout (see §4):

- Offset **+13** lands inside the **16-byte header block** → corrupts the header
  (changes the recognised sector identity).
- Offset **+128** lands inside the **data block** (data starts at +16) → breaks a
  **data checksum**.

Corrupting both guarantees the sector fails regardless of which check Minerva leans
on (the real header has no checksum, but the data block carries two — so the
data-side hit is what reliably trips verify).

---

## 3. In-RAM cartridge image layout (authoritative, from code)

File: **`Firmware/UserInterface.h`**

| Constant | Value | Meaning |
|----------|-------|---------|
| `CARTRIDGE_HEADER_SIZE` | `16` | stored header block per sector |
| `CARTRIDGE_DATA_SIZE` | `612` | stored data block per sector |
| `CARTRIDGE_SECTOR_SIZE` | `628` | stride per sector (16 + 612) |
| `CARTRIDGE_SECTOR_COUNT` | `255` | sectors per cartridge |

**In-RAM image size = 628 × 255 = 160,140 bytes.**

Per-sector record (in `cartridge_image`):

```
offset  0 .. 15   : header block (16 bytes)
            +1     : sector number byte   (read as `secNum`)
            +13    : damaged on sector 13 (header hit)
offset 16 .. 627  : data block (612 bytes)
            +128   : damaged on sector 13 (data hit; = data offset 112)
```

> **Note vs on-disk `.mdv`:** the **in-RAM** layout above (628 B/sector) does **not**
> store the differential-Manchester **preambles** — they are regenerated at playback
> by `write_buffer_set_pair()` (see §5). The classic on-disk **QLay MDV** is the
> ~174,930-byte format (686 B/sector incl. preambles). The loader transforms between
> the two; verify the exact load/save transform against your build before assuming
> byte-for-byte equivalence.

---

## 4. Real Microdrive / Minerva format internals

From QL Forum thread **t=1269** ("Microdrive"), tofro's analysis of how **Minerva
1.98** writes a *real* physical cartridge:

- **Sector header = 14 bytes**, and on the real thing the header has **no checksum**.
- The **data block has two checksums**:
  - first verifies the **file # and block #** are correct,
  - second verifies the **data block contents**.
- **Empty sectors are pre-filled with `$AA55`** during format.
- **Medium name length = 10** bytes.
- In an MDV file there's roughly a **148-byte gap** between sector data and the next
  sector header.

These map directly onto the firmware's choices: corrupting the data region (+128)
breaks one of the two data checksums; corrupting the header region (+13) changes the
recognised sector identity. The header-has-no-checksum detail is why a single
header-only edit isn't trusted to fail verify on its own — the data-side hit is the
reliable one.

---

## 5. PIO timing margins (the *other* failure class)

File: **`Firmware/PIO_machines.pio`** — relevant to intermittent/Minerva-only read
glitches that are **timing**, not format, problems.

- **RX (QL→Pico) `microdrive_read`** runs at **20 PIO clocks/bit** (≈2 MHz for the
  QL's 100 kHz data), where the original design used 16. The extra cycles exist so
  the machine can **resynchronise if the ULA skews the write frequency**. Sampling
  happens mid-second-half of each bit (`[14]` delay).
- **TX (Pico→QL) `microdrive_write`** runs at **16 clocks/bit** (≈1.6 MHz).
- **`microdrive_shift_select`** runs **32× the SER_DATA clock** (~688 kHz for the
  ~21.5 kHz select clock); resynchronises on each clock edge.
- Buffer playback skews **track 2 by 4 bits** (`track2Buffer += 4`) to match the ULA.
- Preamble regenerated as `PREAMBLE_ZERO_BITS = 40` zeros + `PREAMBLE_ONE_BITS = 8`
  ones (`Firmware/SharedBuffers.h`).

Symptom of this class (forum t=4883, later page): occasional load errors attributed
to the device **releasing the bus too soon** because it serves data "instantly"
versus a real tape's slow feed, so the QL can issue a second read before the first is
acknowledged. Fix lives in gap/timing handling, **not** in the format workaround.

---

## 6. Practical recipe for Minerva

1. **To create a usable cartridge under Minerva:** run `FORMAT mdv1_name` **on the
   QL through MicroPicoDrive**. Let the firmware bake in the skip-254 / damage-13
   imperfections, then copy files in.
2. **Then save the cartridge** (button persist to SD). The saved `.mdv` now contains
   the Minerva-friendly bad sector and can be re-imported safely.
3. **Do NOT** import a pristine qlay2/mdvtool image and expect Minerva to accept it —
   `inFormat` never fires for an import, so sector 13 stays intact and sector 254 is
   present → likely `Format failed` / rejection.
4. **To Minerva-proof an external image in software** (no QL format pass): replicate
   the firmware edit on the chosen image — corrupt sector 13's header + data bytes at
   the equivalent offsets, and optionally drop sector 254. Mind the on-disk vs in-RAM
   offset difference (§3) when computing positions in a 686-B/sector `.mdv`.

---

## 7. Failure-mode triage

| Symptom | Likely cause | Fix |
|---------|--------------|-----|
| `Format failed` on `FORMAT mdvN_` | Minerva "perfect cartridge" rejection | Ensure firmware `inFormat` path runs (format on-QL, not import); confirm skip-254 + damage-13 active |
| Boots & `DIR` OK, but won't `EXEC` a program | Minerva 1.98 dataspace/exec quirk (image-independent) | `POKE 53,128` (Minerva 1.98 only) |
| Intermittent read/load errors, retry succeeds | Bus/timing — device releases bus too early | PIO gap/timing margins; not a format issue |
| Imported clean MDV rejected, on-QL format fine | Import path bypasses `inFormat` accommodations | Format on-QL, or pre-damage the image |

---

## 8. Source references

**Firmware (`gusmanb/micropicodrive` @ master):**
- `Firmware/UserInterface.c` — `inFormat` (142, 167, 285), `process_md_read` (~216),
  `process_md_write` (~245), `write_buffer_set` / `write_buffer_set_pair` (~40–98).
- `Firmware/UserInterface.h` — cartridge layout constants (33–36).
- `Firmware/PIO_machines.pio` — RX/TX/shift-select/status state machines + timing.
- `Firmware/MicroDriveControl.{c,h}` — gap/DMA/buffer-set sequencing.
- `Firmware/SharedBuffers.{c,h}` — buffer sizes, `cartridge_image`.

**QL Forum:**
- t=4523 — original MicroPicoDrive thread; "Minerva picky with perfect microdrives".
- t=4883 — "MicroPicoDrive Popopo's version"; Minerva `Format failed` reports,
  bus-timing discussion, file-header (Q-emulator / XTcc) compatibility.
- t=1269 — "Microdrive"; tofro's real-tape / Minerva 1.98 format structure analysis.

---

## 9. Open questions / to verify

- [ ] Exact load/save transform between on-disk `.mdv` (686 B/sector, with preambles)
      and in-RAM `cartridge_image` (628 B/sector, no preambles) — find the loader.
- [ ] Is `inFormat` detection (sector # == 255 marker) robust across JM/JS vs Minerva
      format passes, and with 512 KB expansion present (timing)?
- [ ] Does damaging *only* sector 13 + skipping 254 satisfy all Minerva versions
      (1.97 vs 1.98), or is the choice of sector indices version-sensitive?
- [ ] Confirm whether the Popopo fork (branch 1.4x) carries the same `inFormat`
      accommodations or diverges.
- [ ] Cross-check damage offsets (+13, +128) against the 14-byte real header vs the
      16-byte stored header — account for the 2-byte difference.

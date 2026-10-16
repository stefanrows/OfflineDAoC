# OPEN BG window redesign: renders and preview tool

Mock renders of the OPEN BG window (Custom5, "Kriegstafel") made without the game client. They are an offline
approximation: glyph placement, label alpha, hover and click areas and the Isles skin are only proven in the real client
(checklist in `docs/COMPANION_MANAGER_INTEGRATION.md`, section "OPEN BG window redesign").

| File | What |
| --- | --- |
| `current_lage.png` | the 0.282.0 window (before the redesign) |
| `concept_lage.png`, `concept_setup.png` | the concept A mock from the analysis (`concept.py`, `concept_labels.py`: scratch scripts with fixed paths, kept for the record) |
| `redesign_lage.png`, `redesign_setup.png` | the built 0.283.0 window drawn from the real builder XML and the real server labels |
| `sample-labels-lage.json`, `sample-labels-setup.json` | the label texts 620-984 of those two drawings (input of the tool and its self-test) |

## Preview a window before installing it

The tool is `source/server/tools/render_open_bg_mock.py` (Python 3 with Pillow only). It reads the window XML the client
builder generated, the labels JSON and the stock client UI (textures, fonts), and writes a PNG:

```
python3 source/server/tools/render_open_bg_mock.py \
    --window /mnt/c/OfflineDAoC/playable-dev/stage-companion-manager-0.283.0 \
    --labels docs/open-bg-redesign/sample-labels-lage.json \
    --out /tmp/lage.png            # --skin isles for the Isles file, --ui <client app or ui dir> for another stock UI
```

`--window` takes a stage directory (`atlantis/custom5_window.xml`), a client `ui` directory or the XML file itself. The
default stock UI is `/mnt/c/OfflineDAoC/playable/runtime/client-opendaoc/app` (read only).

Self-test (renders both samples in both skins, checks the PNG size 800 x 680; needs the stock UI, else it prints SKIP):

```
python3 source/server/tools/test_render_open_bg_mock.py
```

## Make labels from a status snapshot

The explicit C# test `DumpLabelsForTheMockRenderer` (`source/server/Tests/UnitTests/UT_OpenBgWindow.cs`) draws an Overview
(first unit opened, Relieve armed, a member selected) and a Setup page (Bot cap selected, a reply) through the real
`OpenBgWindowSession` and writes `overview.json` and `setup.json`. `OPEN_BG_SAMPLE` is an `open-bg-status.json` (for
example a copy of `runtime/server/open-bg-status.json`), `OPEN_BG_LABEL_DUMP` an existing output directory, both as
Windows paths passed through WSLENV:

```
export OFFLINE_DAOC_ROOT=/mnt/c/OfflineDAoC/playable
export OPEN_BG_SAMPLE=$(wslpath -w /path/to/open-bg-status.json) OPEN_BG_LABEL_DUMP=$(wslpath -w /path/to/outdir)
export WSLENV=$WSLENV:OPEN_BG_SAMPLE:OPEN_BG_LABEL_DUMP
tools/dev/winnet.sh test source/server/Tests/Tests.csproj -c Release --no-restore --filter DumpLabelsForTheMockRenderer
```

Copy `overview.json` / `setup.json` over the two `sample-labels-*.json` files to refresh the samples (they hold bot names
of a test snapshot, no account data).

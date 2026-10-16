#!/usr/bin/env python3
"""Self-test of render_open_bg_mock.py: builds the OPEN BG window XML with the client builder, draws the committed
sample labels (docs/open-bg-redesign/sample-labels-*.json) and checks the PNGs exist with the window size.
Run: python3 source/server/tools/test_render_open_bg_mock.py [--ui <client app or ui dir>]
Needs Pillow; the builder's native-patch dependencies are stubbed (the window XML does not use them).
Prints SKIP and exits 0 when no stock client UI is reachable."""
import argparse
import sys
import tempfile
from pathlib import Path
from unittest import mock

from PIL import Image

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[2]
sys.path.insert(0, str(HERE))
for name in ("pefile", "capstone", "keystone"):
    try:
        __import__(name)
    except ImportError:
        sys.modules[name] = mock.MagicMock()
import build_companion_manager_client as builder  # noqa: E402
import render_open_bg_mock as mock_renderer  # noqa: E402


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--ui", default=mock_renderer.DEFAULT_UI)
    args = p.parse_args()
    try:
        mock_renderer.find_ui_dir(args.ui)
    except SystemExit as error:
        print("SKIP", error)
        return
    samples = {"lage": REPO / "docs/open-bg-redesign/sample-labels-lage.json",
               "setup": REPO / "docs/open-bg-redesign/sample-labels-setup.json"}
    with tempfile.TemporaryDirectory() as tmp:
        stage = Path(tmp) / "stage"
        for skin in ("atlantis", "isles"):
            (stage / skin).mkdir(parents=True)
            (stage / skin / "custom5_window.xml").write_bytes(builder.open_bg_window())
        expected = (builder.BG_WIDTH + 40, builder.BG_HEIGHT + 40)
        for skin in ("atlantis", "isles"):
            for name, labels in samples.items():
                labels_dict = mock_renderer.load_labels(labels)
                assert len(labels_dict) == 365 and min(labels_dict) == 620 and max(labels_dict) == 984, name
                out = Path(tmp) / f"{skin}-{name}.png"
                size = mock_renderer.render_file(stage, labels, out, args.ui, skin)
                assert out.is_file() and out.stat().st_size > 10000, out
                assert size == expected == Image.open(out).size, (size, expected)
                # not blank: the window draws many distinct colours
                assert len(Image.open(out).getcolors(maxcolors=1 << 20)) > 100, out
                print(f"PASS  {skin} {name}: {size[0]}x{size[1]}")
    print("render_open_bg_mock: all checks passed")


if __name__ == "__main__":
    main()

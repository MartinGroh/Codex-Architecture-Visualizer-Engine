"""Generate the portable 16x16 MSB-first monochrome assets. Requires Pillow for PNG previews."""
from pathlib import Path
from PIL import Image

PATTERNS = {
    "main_agent": [
        "0000000110000000", "0000000110000000", "0000000110000000", "0001111111111000",
        "0011000000001100", "0010000000000100", "0110011001100110", "0110011001100110",
        "0110000000000110", "0010001111000100", "0011000000001100", "0001111111111000",
        "0000011111100000", "0000110000110000", "0000111111110000", "0000000000000000",
    ],
    "subagent": [
        "0001100000011000", "0000110000110000", "0000011111100000", "0001110000111000",
        "0011000000001100", "0110000000000110", "1100011001100011", "1100011001100011",
        "0110000000000110", "0011001111001100", "0001110000111000", "0000011111100000",
        "0001111111111000", "0000111111110000", "0000000000000000", "0000011111100000",
    ],
}

if __name__ == "__main__":
    root=Path(__file__).resolve().parent
    header=["/* MIT. 16x16, one uint16_t per row, bit15 is the leftmost pixel. */", "#ifndef CAVE_AGENT_FLOW_BITMAPS_H", "#define CAVE_AGENT_FLOW_BITMAPS_H", "#include <stdint.h>", "#define CAVE_AGENT_FLOW_ICON_WIDTH 16", "#define CAVE_AGENT_FLOW_ICON_HEIGHT 16"]
    for name, rows in PATTERNS.items():
        assert len(rows)==16 and all(len(row)==16 and set(row)<=set("01") for row in rows)
        values=[int(row,2) for row in rows]
        header.append("static const uint16_t cave_%s_bitmap[16] = { %s };" % (name, ", ".join("0x%04X" % value for value in values)))
        preview=Image.new("RGBA",(16,16),(0,0,0,0))
        for y,row in enumerate(rows):
            for x,value in enumerate(row):
                if value=="1": preview.putpixel((x,y),(77,148,219,255))
        preview.save(root/(name.replace("_","-")+"-16.png"))
    header.append("#endif")
    (root/"agent_flow_bitmaps.h").write_bytes(("\n".join(header)+"\n").encode("utf-8"))

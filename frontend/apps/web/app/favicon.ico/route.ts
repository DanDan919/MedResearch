import { createElement } from "react";
import { ImageResponse } from "next/og";

export function GET() {
  return new ImageResponse(
    createElement("div", {
      style: {
        width: "100%",
        height: "100%",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        background: "#134e4a",
        color: "white",
        fontSize: 24,
        fontWeight: 700
      }
    }, "M"),
    { width: 32, height: 32 }
  );
}

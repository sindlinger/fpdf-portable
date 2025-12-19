import argparse
import json
from datetime import datetime
from typing import Any, Dict, List

import pandas as pd
import psycopg2


def load_process_rows(pg_uri: str, source_contains: str | None) -> List[Dict[str, Any]]:
    sql = "select process_number, source, json from processes"
    params = []
    if source_contains:
        sql += " where source ilike %s"
        params.append(f"%{source_contains}%")
    with psycopg2.connect(pg_uri) as conn:
        with conn.cursor() as cur:
            cur.execute(sql, params)
            rows = []
            for process_number, source, js in cur.fetchall():
                rows.append({"process_number": process_number, "source": source or "", "json": js})
            return rows


def iter_fields(process_number: str, source: str, js: Any):
    try:
        data = json.loads(js) if isinstance(js, str) else js
    except Exception:
        return
    docs = data.get("documents") or []
    for di, doc in enumerate(docs):
        fields = doc.get("fields") or {}
        for field, info in fields.items():
            ev = (info or {}).get("evidence") or {}
            bbox = ev.get("bboxN")
            yield {
                "process_number": process_number,
                "source": source,
                "doc_index": di,
                "doc_type": doc.get("docType"),
                "start_page": doc.get("startPage1"),
                "end_page": doc.get("endPage1"),
                "field": field,
                "value": (info or {}).get("value"),
                "method": (info or {}).get("method"),
                "confidence": (info or {}).get("confidence"),
                "page1": ev.get("page1"),
                "bbox": bbox,
            }


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--pg", default="postgres://fpdf:fpdf@localhost:5432/fpdf")
    ap.add_argument("--source-contains", default=None)
    ap.add_argument("--out-dir", default="data/out-test")
    args = ap.parse_args()

    rows = load_process_rows(args.pg, args.source_contains)
    records = []
    for r in rows:
        for item in iter_fields(r["process_number"], r["source"], r["json"]):
            bbox = item.pop("bbox", None)
            if bbox:
                x0 = bbox.get("x0")
                y0 = bbox.get("y0")
                x1 = bbox.get("x1")
                y1 = bbox.get("y1")
                if x0 is None or y0 is None or x1 is None or y1 is None:
                    continue
                y_center = (y0 + y1) / 2.0
                height = y1 - y0
                if y_center >= 0.66:
                    y_band = "top"
                elif y_center >= 0.33:
                    y_band = "mid"
                else:
                    y_band = "bottom"
                item.update({
                    "x0": x0, "y0": y0, "x1": x1, "y1": y1,
                    "y_center": y_center, "height": height, "y_band": y_band
                })
                records.append(item)

    df = pd.DataFrame(records)
    if df.empty:
        print("no bbox records")
        return

    # Outlier detection per field (IQR)
    def mark_outliers(group: pd.DataFrame):
        q1 = group["y_center"].quantile(0.25)
        q3 = group["y_center"].quantile(0.75)
        iqr = q3 - q1
        low = q1 - 1.5 * iqr
        high = q3 + 1.5 * iqr
        return ((group["y_center"] < low) | (group["y_center"] > high)).astype(int)

    df["is_outlier"] = df.groupby("field", group_keys=False).apply(mark_outliers)

    # Summary
    summary = df.groupby("field").agg(
        count=("field", "count"),
        outliers=("is_outlier", "sum"),
        y_center_mean=("y_center", "mean"),
        y_center_median=("y_center", "median"),
        y_center_min=("y_center", "min"),
        y_center_max=("y_center", "max"),
        height_mean=("height", "mean"),
        height_median=("height", "median"),
    ).reset_index()

    ts = datetime.now().strftime("%Y%m%d_%H%M%S")
    out_csv = f"{args.out_dir}/bbox_report_{ts}.csv"
    out_xlsx = f"{args.out_dir}/bbox_report_{ts}.xlsx"

    df.to_csv(out_csv, index=False)
    with pd.ExcelWriter(out_xlsx, engine="openpyxl") as writer:
        df.to_excel(writer, sheet_name="bbox", index=False)
        summary.to_excel(writer, sheet_name="summary", index=False)

    print(out_csv)
    print(out_xlsx)


if __name__ == "__main__":
    main()

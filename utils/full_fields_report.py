import argparse
import json
from datetime import datetime
from typing import Any, Dict, List

import pandas as pd
import psycopg2

FIELDS = [
    "PROCESSO_ADMINISTRATIVO",
    "PROCESSO_JUDICIAL",
    "VARA",
    "COMARCA",
    "PROMOVENTE",
    "PROMOVIDO",
    "PERITO",
    "CPF_PERITO",
    "ESPECIALIDADE",
    "ESPECIE_DA_PERICIA",
    "VALOR_ARBITRADO_JZ",
    "VALOR_ARBITRADO_DE",
    "VALOR_ARBITRADO_CM",
    "VALOR_TABELADO_ANEXO_I",
    "ADIANTAMENTO",
    "PERCENTUAL",
    "PARCELA",
    "DATA",
    "ASSINANTE",
    "NUM_PERITO",
]


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


def iter_documents(process_number: str, source: str, js: Any):
    try:
        data = json.loads(js) if isinstance(js, str) else js
    except Exception:
        return
    docs = data.get("documents") or []
    for di, doc in enumerate(docs):
        yield di, doc


def get_field_info(doc: Dict[str, Any], field: str) -> Dict[str, Any]:
    fields = doc.get("fields") or {}
    info = fields.get(field) or {}
    ev = (info.get("evidence") or {}) if isinstance(info, dict) else {}
    bbox = ev.get("bboxN") if isinstance(ev, dict) else None
    return {
        "value": info.get("value", "-") if isinstance(info, dict) else "-",
        "confidence": info.get("confidence", 0.0) if isinstance(info, dict) else 0.0,
        "method": info.get("method", "not_found") if isinstance(info, dict) else "not_found",
        "page1": ev.get("page1"),
        "bbox": bbox,
        "snippet": ev.get("snippet")
    }


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--pg", default="postgres://fpdf:fpdf@localhost:5432/fpdf")
    ap.add_argument("--source-contains", default=None)
    ap.add_argument("--out-dir", default="data/out-report")
    args = ap.parse_args()

    rows = load_process_rows(args.pg, args.source_contains)
    long_records = []
    wide_records = []

    for r in rows:
        for doc_index, doc in iter_documents(r["process_number"], r["source"], r["json"]):
            base = {
                "process_number": r["process_number"],
                "source": r["source"],
                "doc_index": doc_index,
                "doc_type": doc.get("docType"),
                "start_page": doc.get("startPage1"),
                "end_page": doc.get("endPage1"),
            }

            wide_row = dict(base)
            for field in FIELDS:
                info = get_field_info(doc, field)

                # long
                bbox = info.pop("bbox", None)
                if bbox and isinstance(bbox, dict):
                    info.update({
                        "x0": bbox.get("x0"),
                        "y0": bbox.get("y0"),
                        "x1": bbox.get("x1"),
                        "y1": bbox.get("y1"),
                    })
                long_records.append({
                    **base,
                    "field": field,
                    **info
                })

                # wide
                wide_row[f"{field}__value"] = info.get("value", "-")
                wide_row[f"{field}__confidence"] = info.get("confidence", 0.0)
                wide_row[f"{field}__method"] = info.get("method", "not_found")
                wide_row[f"{field}__page1"] = info.get("page1")
            wide_records.append(wide_row)

    df_long = pd.DataFrame(long_records)
    df_wide = pd.DataFrame(wide_records)

    if df_long.empty:
        print("no records")
        return

    summary = df_long.groupby("field").agg(
        count=("field", "count"),
        missing=("value", lambda s: (s == "-").sum()),
        mean_conf=("confidence", "mean"),
        min_conf=("confidence", "min"),
        max_conf=("confidence", "max"),
    ).reset_index()

    ts = datetime.now().strftime("%Y%m%d_%H%M%S")
    out_csv = f"{args.out_dir}/full_fields_{ts}.csv"
    out_xlsx = f"{args.out_dir}/full_fields_{ts}.xlsx"

    df_long.to_csv(out_csv, index=False)
    with pd.ExcelWriter(out_xlsx, engine="openpyxl") as writer:
        df_long.to_excel(writer, sheet_name="long", index=False)
        df_wide.to_excel(writer, sheet_name="wide", index=False)
        summary.to_excel(writer, sheet_name="summary", index=False)

    print(out_csv)
    print(out_xlsx)


if __name__ == "__main__":
    main()

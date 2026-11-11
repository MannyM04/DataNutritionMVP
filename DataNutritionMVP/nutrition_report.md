# Clinical Data Nutrition Label & Audit Report

* **Source Dataset:** `dirty_clinical_data.csv`
* **Audit Timestamp (UTC):** `2026-11-11 19:17:12 UTC`
* **Overall Dataset Health Score:** **99.05%** (`WARN (REQUIRES CLEANING)`)

## 1. Engine Telemetry
| Metric | Value |
| :--- | :--- |
| **Total Records Audited** | `1,000,000` rows (`4,000,000` clinical data cells) |
| **Cold-Start Execution Time** | `427 ms` |
| **Heap Memory Footprint** | `66.62 KB` (`68,224 bytes`) |
| **Total Clean Cells** | `3,962,000` |
| **Total Missing / Corrupt Cells** | `30,000` |
| **Total Physiological Outliers** | `8,000` |

## 2. Column-by-Column Nutrition Breakdown
| Clinical Field | Gate | Validity % | Valid Rows | Missing / Corrupt | Outliers | Mean | Observed Range | Allowed Bounds |
| :--- | :---: | ---: | ---: | ---: | ---: | ---: | :---: | :---: |
| **Age (Years)** | `WARN` | `98.80%` | `988,000` | `10,000` | `2,000` | `53.5` | `18 - 89` | `[0, 120]` |
| **Heart Rate (BPM)** | `WARN` | `98.80%` | `988,000` | `10,000` | `2,000` | `82.0` | `55 - 109` | `[30, 220]` |
| **Systolic BP (mmHg)** | `WARN` | `98.80%` | `988,000` | `10,000` | `2,000` | `127.0` | `95 - 159` | `[70, 250]` |
| **Diastolic BP (mmHg)** | `PASS` | `99.80%` | `998,000` | `0` | `2,000` | `79.5` | `60 - 99` | `[40, 150]` |

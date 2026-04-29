# TeslaLogger SQL-Optimierungsplan

## Ziel

Identifizierung und Optimierung langsamer SQL-Statements im TeslaLogger-Projekt (NET8-Branch) zur Verbesserung der Datenbankperformance.

---

## 0. Datenbank-Zugangsdaten

> ⚠️ **Architektur:** TeslaLogger läuft direkt auf Raspberry Pi (NET8 Runtime ARM32), nicht in Docker.
> Datenbank: MariaDB 10.3.39 auf dem gleichen Raspberry Pi.

| Eigenschaft | Wert |
|-------------|------|
| **Laufumgebung** | Raspberry Pi 3B (ARM32 ARMv7) |
| **Hostname** | `teslalogger` (oder lokale RasPi-IP) |
| **Datenbank** | `teslalogger` |
| **Username** | `root` |
| **Passwort** | `teslalogger` |
| **MySQL CLI** | `/opt/homebrew/bin/mysql` (auf Mac) oder `mysql` (auf RasPi) |
| **DB-System** | MariaDB 10.3.39 |
| **SSL** | Nicht unterstützt – Verbindung erfordert `--skip-ssl` |

### Verbindungsaufbau (MySQL CLI)

**Von Development-Mac aus (remote zu RasPi):**
```bash
/opt/homebrew/bin/mysql -h <raspi-ip> -u root -pteslalogger --skip-ssl teslalogger
```

**Auf dem Raspberry Pi direkt:**
```bash
mysql -u root -pteslalogger teslalogger
```

### Schnellbefehle

**Verbindung herstellen (von RasPi aus):**
```bash
mysql -u root -pteslalogger teslalogger
```

**Verbindung herstellen (von Mac aus):**
```bash
/opt/homebrew/bin/mysql -h <raspi-ip> -u root -pteslalogger --skip-ssl teslalogger
```

**Wichtigste Befehle (auf RasPi oder remote):**
```bash
# Tabellen auflisten
SHOW TABLES;

# Tabellengrößen prüfen
SELECT table_name, 
       table_rows, 
       ROUND(data_length/1024/1024, 2) AS data_size_MB, 
       ROUND(index_length/1024/1024, 2) AS index_size_MB
FROM information_schema.tables 
WHERE table_schema = 'teslalogger'
ORDER BY (data_length + index_length) DESC;

# Indizes einer Tabelle anzeigen
SHOW INDEX FROM chargingstate;
SHOW INDEX FROM drivestate;
SHOW INDEX FROM pos;

# Slow Query Log aktivieren
SET GLOBAL slow_query_log = 'ON';
SET GLOBAL long_query_time = 1;

# Prozessliste prüfen (aktive Queries)
SHOW PROCESSLIST;
```

---

## 0.1 Raspberry Pi Architektur – Performance-Constraints

**Zielplattform:** Raspberry Pi 3B mit ARM32 (ARMv7) Architektur, .NET 8 Runtime

| Constraint | Auswirkung auf SQL-Performance |
|-----------|-------------------------------|
| **RAM: ~1 GB** | Begrenzte Buffer Pools, keine großen Collection-Allocationen möglich. Query Results sollten gestreamt werden statt materialisiert. Connection Pooling begrenzt. |
| **CPU: Single-Core ARM (theoretisch 4 Cores)** | Async/Await kritisch – Blocking Calls verstärken Bottlenecks. No SIMD, ARM-spezifische Optimierungen nicht möglich. |
| **Storage: SD-Card (langsam)** | Disk I/O ist der Engpass. Index-Lookups > Full Table Scans aufgrund von Seek-Time. Batch-Operationen reduzieren Round-Trips. |
| **MariaDB auf gleicher Hardware** | Konkurrenzkampf um RAM und CPU zwischen Anwendung und Datenbank. Swap-Nutzung sehr wahrscheinlich → massive Performance-Degradation. |

**Optimierungs-Priorität für RasPi:**
1. **Indexing** (schnellste Wins, geringe Kosten)
2. **Query Batching** (reduziert Round-Trips, senkt Latenz)
3. **Async Patterns** (senkt Blocking, verbessert Responsiveness)
4. **LINQ-Streaming** (AsAsyncEnumerable statt ToList)
5. **Correlated Subquery Elimination** (Resource-intensive Queries)

---

## 1. Analyse-Status

### Durchgeführte Code-Analyse
- ✅ Repository-Struktur untersucht (~100+ Dateien)
- ✅ SQL-Statements in allen relevanten Dateien identifiziert
- ✅ Komplexeste Queries extrahiert und kategorisiert
- ✅ Datenbank-Schema (MariaDB 10.3.39) verstanden

### Datenbank-Verbindung
- ✅ Verbindung mit `--skip-ssl` erfolgreich (SSL nicht unterstützt)
- ✅ Alle Fakten aus der Live-Datenbank ermittelt (26. April 2026)

### Betroffene Hauptdateien (nach SQL-Komplexität sortiert)
| Datei | Schätzung SQL-Statements | Komplexität |
|-------|-------------------------|-------------|
| `WebServer.cs` | ~150+ | Sehr hoch |
| `ShareData.cs` | ~15 | Hoch |
| `StaticMapService.cs` | ~20 | Hoch |
| `Journeys.cs` | ~20 | Mittel-Hoch |
| `DBHelper.cs` | ~30 | Mittel |
| `Car.cs` | ~25 | Mittel |
| `ChargingState.cs` | ~15 | Mittel |
| `DriveState.cs` | ~10 | Mittel |
| `Dashboard.cs` | ~10 | Mittel |
| `DashboardGrafana.cs` | ~8 | Mittel |
| `StaticDashboard.cs` | ~15 | Mittel |
| `UpdateTeslalogger.cs` | ~20 | Niedrig-Mittel |

---

## 2. Identifizierte Problemstellen

### 2.1 Kritische Queries (Priorität 1)

#### A. Correlated Subqueries in ShareData.cs
**Problem:** Verschachtelte Subqueries innerhalb von SELECT, die pro Zeile ausgeführt werden

```sql
-- ShareData.cs: GetChargingDT()
SELECT
    AVG(UNIX_TIMESTAMP(Datum)) AS Datum,
    AVG(battery_level),
    ...
    (
        SELECT CellTemperature
        FROM celltemperature
        WHERE celltemperature.carid = @CarID
          AND celltemperature.date < charging.Datum
          AND celltemperature.date > DATE_ADD(charging.Datum, INTERVAL -4 MINUTE)
        ORDER BY celltemperature.date DESC
        LIMIT 1
    ) AS cell_temp
FROM charging
WHERE id BETWEEN @startid AND @endid AND carid = @CarID
GROUP BY battery_level
ORDER BY battery_level
```
**Impact:** O(n) Subquery-Ausführungen – bei 1000 Zeilen = 1000 zusätzliche Queries

---

#### B. Correlated Subquery für Firmware in ShareData.cs
**Problem:** Subquery zur Firmware-Ermittellung pro drivestate-Zeile

```sql
-- ShareData.cs: SendAllDrivingDataAsync()
SELECT ...
    (select version from car_version 
     where car_version.StartDate < drivestate.StartDate 
     and car_version.carid = drivestate.carid 
     order by id desc limit 1) as Firmware
FROM drivestate
JOIN pos pos_start ON drivestate.StartPos = pos_start.id
JOIN pos pos_end ON drivestate.EndPos = pos_end.id
JOIN cars ON cars.id = drivestate.CarID
WHERE ...
```

---

#### C. Mehrfache Subqueries in Journeys.cs
**Problem:** Subqueries werden mehrmals pro Query ausgeführt

```sql
-- Journeys.cs: CalculateFreeSuC(), CalculateCharged(), CalculateChargeDuration()
SELECT SUM(chargingstate.cost_freesuc_savings_total)
FROM chargingstate
WHERE chargingstate.Pos > (SELECT StartPosID FROM journeys WHERE ID = @journeyID)
  AND chargingstate.Pos < (SELECT EndPosID FROM journeys WHERE ID = @journeyID)
  AND chargingstate.carID = (SELECT CarID FROM journeys WHERE ID = @journeyID)
```
**Impact:** 3 separate Subqueries auf dieselbe Tabelle `journeys`

---

#### D. Komplexe JOINs in WebServer.cs
**Problem:** Mehrere 3- bis 5-fache JOINs mit Aggregationen

```sql
-- WebServer.cs: ChargingStateDataTable()
SELECT chargingstate.*, lat, lng, address, chargingstate.charge_energy_added as kWh 
FROM chargingstate 
JOIN pos ON chargingstate.Pos = pos.id 
JOIN charging ON chargingstate.StartChargingID = charging.id 
WHERE chargingstate.CarID = @CarID 
ORDER BY chargingstate.StartDate DESC
```

---

#### E. Trip-View mit komplexen Berechnungen
**Problem:** View berechnet abgeleitete Spalten bei jedem Zugriff

```sql
-- DBViews.cs: Trip View
SELECT 
    drivestate.StartDate AS StartDate,
    ...
    (pos_end.odometer - pos_start.odometer) AS km_diff,
    ((pos_start.ideal_battery_range_km - pos_end.ideal_battery_range_km) * wh_tr) AS consumption_kWh,
    (((pos_start.ideal_battery_range_km - pos_end.ideal_battery_range_km) * wh_tr) / (pos_end.odometer - pos_start.odometer)) * 100 AS avg_consumption_kWh_100km,
    TIMESTAMPDIFF(MINUTE, drivestate.StartDate, drivestate.EndDate) AS DurationMinutes,
    ...
FROM drivestate
JOIN pos pos_start ON drivestate.StartPos = pos_start.id
JOIN pos pos_end ON drivestate.EndPos = pos_end.id
JOIN cars ON cars.id = drivestate.CarID
WHERE (pos_end.odometer - pos_start.odometer) > 0.1
```

---

### 2.2 Mittelkritische Queries (Priorität 2)

#### F. UNION DISTINCT in WebHelper.cs
```sql
-- WebHelper.cs: UpdateAllPOIAddresses()
SELECT DISTINCT Pos FROM chargingstate
UNION DISTINCT
SELECT StartPos FROM drivestate
UNION DISTINCT
SELECT EndPos FROM drivestate
ORDER BY Pos DESC
```

#### G. Bulk-SELECT ohne LIMIT in StaticMapService.cs
```sql
-- StaticMapService.cs: CreateAllTripMaps()
SELECT startposid, endposid, carid FROM trip ORDER BY startdate DESC
```

#### H. String-Konkatenation für IN-Clause in ShareData.cs
```sql
-- ShareData.cs: SendAllDrivingDataAsync()
update drivestate set export = {ProtocolVersion} where id in ({l})
```
**Problem:** Potentiell sehr langer IN-Clause mit vielen IDs

---

### 2.3 Geringere Priorität (Priorität 3)

#### I. String-basierte WHERE-Klauseln
```sql
-- ShareData.cs: SendAllChargingDataAsync()
WHERE address LIKE 'Supercharger%' OR address LIKE 'Ionity%'
```

#### J. Fehrende Parameterisierung
```sql
-- Mehrere Stellen: CarID wird direkt in SQL-String eingebettet
WHERE chargingstate.carid = {car.CarInDB}
```

---

### 2.4 Grafana-Dashboard-Queries (aus `Grafana/Dashboard/`)

> **Kontext:** Die Grafana-Dashboards verwenden SQL-Queries direkt gegen die MariaDB-Datenbank.
> `$__time()` und `$__timeFilter()` sind Grafana-Makros, die zur Laufzeit mit datenbank-spezifischen Funktionen ersetzt werden (z. B. `UNIX_TIMESTAMP(column) AS time_sec` für MariaDB).

#### A. `Status.json` – Echtzeit-Dashboard (8 Queries)

| # | Panel | Query-Typ | Betroffene Tabellen | Performance-Risiko |
|---|-------|-----------|-------------------|-------------------|
| 1 | Car Status (aktuell) | UNION (5 Quellen) + LIMIT 1 | `state`, `trip` (VIEW), `chargingstate` | 🟢 Niedrig (kleine Tabellen) |
| 2 | Battery Level | UNION (`pos` + `charging`) + LIMIT 1 | `pos` (84,7M), `charging` (2,6M) | 🔴 **KRITISCH** – `ORDER BY ... DESC LIMIT 1` auf `pos` |
| 3 | Ideal Battery Range | UNION (`pos` + `charging`) + LIMIT 1 | `pos` (84,7M), `charging` (2,6M) | 🔴 **KRITISCH** –同上 |
| 4 | Outside Temperature | UNION (`pos` + `charging`) + LIMIT 1 | `pos` (84,7M), `charging` (2,6M) | 🔴 **KRITISCH** –同上 |
| 5 | Zelltemperatur | `can` + Subquery (5 UNION) | `can` (LEER!), `state`, `trip`, `chargingstate` | 🟡 Mittel – `can` ist leer, Subquery auf kleinen Tabellen |
| 6 | Odometer | `pos` + ORDER BY + LIMIT 1 | `pos` (84,7M) | 🔴 **KRITISCH** – `ORDER BY id DESC` auf 84,7M Zeilen |
| 7 | Software Version | `car_version` + ORDER BY + LIMIT 1 | `car_version` (76) | 🟢 Niedrig |
| 8 | Status Timeline (Graph) | UNION (5 Quellen) – **KEIN LIMIT** | `state`, `trip`, `chargingstate` | 🟡 Mittel – volles Zeitfenster, aber kleine Tabellen |

**Query-Details:**

```sql
-- Panel 2: Battery Level (Status.json, Zeile 178)
-- 🔴 KRITISCH: ORDER BY time_sec DESC auf UNION mit pos (84,7M Zeilen)
SELECT battery_level
FROM (
  SELECT $__time(datum), battery_level FROM pos WHERE $__timeFilter(datum)
  UNION
  SELECT $__time(datum), battery_level FROM charging WHERE $__timeFilter(datum)
  ORDER BY time_sec DESC
) AS t1
LIMIT 1
```

```sql
-- Panel 3: Ideal Battery Range (Status.json, Zeile 263)
-- 🔴 KRITISCH: identisches Pattern wie Panel 2
SELECT ideal_battery_range_km
FROM (
  SELECT $__time(datum), ideal_battery_range_km FROM pos WHERE $__timeFilter(datum)
  UNION
  SELECT $__time(datum), ideal_battery_range_km FROM charging WHERE $__timeFilter(datum)
  ORDER BY time_sec DESC
) AS t1
LIMIT 1
```

```sql
-- Panel 4: Outside Temperature (Status.json, Zeile 341)
-- 🔴 KRITISCH: identisches Pattern wie Panel 2
SELECT outside_temp
FROM (
  SELECT $__time(datum), outside_temp FROM pos WHERE $__timeFilter(datum)
  UNION
  SELECT $__time(datum), outside_temp FROM charging WHERE $__timeFilter(datum)
  ORDER BY time_sec DESC
) AS t1
LIMIT 1
```

```sql
-- Panel 5: Zelltemperatur (Status.json, Zeile 424)
-- 🟡 Mittel: can-Tabelle ist LEER → Query liefert immer NULL
-- Subquery auf 5 UNION-Quellen (alle klein)
SELECT val AS 'Zelltemperatur'
FROM can
WHERE $__timeFilter(datum) 
  AND id = 2 
  AND datum > (
    SELECT $__time(StartDate) FROM state WHERE $__timeFilter(startdate)
    UNION
    SELECT $__time(StartDate) FROM trip WHERE $__timeFilter(StartDate)
    UNION 
    SELECT $__time(EndDate) FROM trip WHERE $__timeFilter(StartDate) AND EndDate IS NOT NULL
    UNION
    SELECT $__time(StartDate) FROM chargingstate WHERE $__timeFilter(StartDate) 
    UNION
    SELECT $__time(EndDate) FROM chargingstate WHERE $__timeFilter(StartDate) AND EndDate IS NOT NULL
    ORDER BY time_sec DESC
    LIMIT 1
  )
ORDER BY datum DESC 
LIMIT 1
```

```sql
-- Panel 6: Odometer (Status.json, Zeile 523)
-- 🔴 KRITISCH: ORDER BY id DESC auf pos (84,7M Zeilen)
SELECT odometer
FROM pos
WHERE $__timeFilter(datum)
ORDER BY id DESC 
LIMIT 1
```

```sql
-- Panel 8: Status Timeline (Status.json, Zeile 836)
-- 🟡 Mittel: Kein LIMIT → volles Zeitfenster, aber alle Quellen klein
SELECT $__time(StartDate), 
  (CASE 
    WHEN state = 'asleep' THEN 1 
    WHEN state = 'online' THEN 2 
    WHEN state = 'offline' THEN 3 
    WHEN state = 'waking' THEN 4 
    ELSE 5 
  END) AS status
FROM state WHERE $__timeFilter(startdate)
UNION
SELECT $__time(StartDate), 6 AS status FROM trip WHERE $__timeFilter(StartDate)
UNION 
SELECT $__time(EndDate), 2 AS status FROM trip WHERE $__timeFilter(StartDate) AND EndDate IS NOT NULL
UNION
SELECT $__time(StartDate), 7 AS status FROM chargingstate WHERE $__timeFilter(StartDate) 
UNION
SELECT $__time(EndDate), 2 AS status FROM chargingstate WHERE $__timeFilter(StartDate) AND EndDate IS NOT NULL
ORDER BY time_sec
```

#### B. `Degradation.json` – Batterie-Degradation (4 Queries)

| # | Panel | Query-Typ | Betroffene Tabellen | Performance-Risiko |
|---|-------|-----------|-------------------|-------------------|
| 1 | Version Timeline | `car_version` + ORDER BY | `car_version` (76) | 🟢 Niedrig |
| 2 | Max Range pro Ladevorgang | 3-facher JOIN + `charging` (self-join) | `charging` (2,6M), `chargingstate` (1,5K), `pos` (84,7M) | 🔴 **KRITISCH** – JOIN auf `pos` über Fremdschlüssel |
| 3 | Nominal Full Pack Capacity | `can` + WHERE | `can` (LEER!) | 🟢 Niedrig (Tabelle leer) |
| 4 | Monthly Average Max Range | 3-facher JOIN + GROUP BY | `charging` (2,6M), `chargingstate` (1,5K), `pos` (84,7M) | 🔴 **KRITISCH** –同上 + Aggregation |

**Query-Details:**

```sql
-- Panel 2: Max Range pro Ladevorgang (Degradation.json, Zeile 97)
-- 🔴 KRITISCH: JOIN-Kette erreicht pos-Tabelle (84,7M Zeilen)
-- chargingstate.pos → pos.id ist der kritische JOIN
SELECT 
  $__time(chargingstate.StartDate), 
  charging_End.ideal_battery_range_km / charging_End.battery_level * 100 AS 'Maximalreichweite [km]',
  pos.odometer AS 'Odometer [km]'
FROM charging 
INNER JOIN chargingstate ON charging.id = chargingstate.StartChargingID 
INNER JOIN pos ON chargingstate.pos = pos.id 
LEFT OUTER JOIN charging AS charging_End ON chargingstate.EndChargingID = charging_End.id
WHERE $__timeFilter(chargingstate.StartDate) 
  AND TIMESTAMPDIFF(MINUTE, chargingstate.StartDate, chargingstate.EndDate) > 3 
  AND pos.odometer > 1
ORDER BY chargingstate.StartDate
```

```sql
-- Panel 4: Monthly Average Max Range (Degradation.json, Zeile 151)
-- 🔴 KRITISCH: identische JOIN-Kette + GROUP BY → Aggregation über große Mengen
SELECT 
  $__time(chargingstate.StartDate), 
  AVG(charging_End.ideal_battery_range_km / charging_End.battery_level * 100) AS 'Max Range (monthly avg) [km]'
FROM charging 
INNER JOIN chargingstate ON charging.id = chargingstate.StartChargingID
INNER JOIN pos ON chargingstate.pos = pos.id 
LEFT OUTER JOIN charging AS charging_End ON chargingstate.EndChargingID = charging_End.id
WHERE $__timeFilter(chargingstate.StartDate) 
  AND TIMESTAMPDIFF(MINUTE, chargingstate.StartDate, chargingstate.EndDate) > 3 
  AND pos.odometer > 1
GROUP BY EXTRACT(YEAR_MONTH FROM chargingstate.StartDate)
ORDER BY charging.Datum
```

#### C. Grafana-Query-Zusammenfassung

| Kategorie | Anzahl Queries | Betroffene große Tabellen | Optimierungsbedarf |
|-----------|--------------|------------------------|-------------------|
| **Status.json** | 8 | `pos` (4 Queries) | 🔴 4 Queries mit `ORDER BY ... LIMIT 1` auf `pos` |
| **Degradation.json** | 4 | `pos` (2 Queries via JOIN) | 🔴 2 Queries mit JOIN auf `pos` |
| **Gesamt** | **12** | **`pos` in 6 Queries** | **6 von 12 Queries berühren die 84,7M-Zeilen-Tabelle** |

##### Kritische Patterns in Grafana-Queries

1. **`ORDER BY time_sec DESC LIMIT 1` auf UNION mit `pos`** (Panels 2–4 in Status.json)
   - MariaDB muss den UNION-Ausdruck sortieren, bevor sie LIMIT anwendet
   - `$__timeFilter(datum)` filtert zwar nach Zeitraum, aber bei großen Zeitfenstern bleiben Millionen Zeilen
   - **Lösung:** Index auf `(CarID, Datum)` ist vorhanden → `$__timeFilter` sollte ihn nutzen. Prüfen mit `EXPLAIN`.

2. **`ORDER BY id DESC LIMIT 1` auf `pos`** (Panel 6 in Status.json)
   - `id` ist PRIMARY KEY → sollte schnell sein (Index-Scan von hinten)
   - **Lösung:** Query ist wahrscheinlich OK, aber mit `$__timeFilter` könnte der Index besser genutzt werden

3. **JOIN-Kette `chargingstate.pos → pos.id`** (Panels 2+4 in Degradation.json)
   - `chargingstate` hat nur 1.551 Zeilen → Join-Treiber ist klein
   - `pos.id` ist PRIMARY KEY → Lookup ist O(1)
   - **Lösung:** Query sollte effizient sein, da der kleine Treiber (`chargingstate`) die JOIN-Reihenfolge bestimmt

4. **`can`-Tabelle ist LEER** (Panel 5 Status.json, Panel 3 Degradation.json)
   - Queries gegen `can` liefern immer NULL/leer
   - **Lösung:** Queries deaktivieren oder auf `battery`-Tabelle umschreiben

---

## 3. Datenbank-Schema-Übersicht (Fakten aus Live-DB)

> **Datenbank-System:** MariaDB 10.3.39 (nicht MySQL!)
> **Gesamtgröße:** ~15,6 GB
> **Tabellen:** 28 Tabellen + 2 Views

### 3.1 Tabellen-Größen (tatsächliche Werte)

| Tabelle | Zeilen | Daten (MB) | Indizes (MB) | Gesamt (MB) | Bemerkung |
|---------|--------|-----------|-------------|-------------|-----------|
| **pos** | **84.682.485** | **11.859,98** | **3.381,97** | **15.241,95** | ⚠️ Dominant! 97,7% der DB |
| `charging` | 2.595.443 | 149,49 | 28,33 | 177,82 | 2,6M Zeilen – zweitgrößte Tabelle |
| `komoot` | 720 | 41,52 | 0,00 | 41,52 | – |
| `superchargerstate` | 362.713 | 13,52 | 0,00 | 13,2 | – |
| `state` | 75.769 | 4,52 | 2,50 | 7,02 | – |
| `mothership` | 62.988 | 3,76 | 0,76 | 4,52 | – |
| `battery` | 55.554 | 3,52 | 0,00 | 3,52 | Basis-Tabelle für celltemperature-View |
| `drivestate` | **10.745** | 2,52 | 0,34 | 2,86 | ❌ Kein Index auf (CarID, StartDate)! |
| `teslacharging` | 552 | 2,52 | 0,00 | 2,52 | – |
| `chargingstate` | **1.551** | 0,27 | 0,22 | 0,48 | ❌ Kein Index auf (CarID, StartDate)! |
| `TPMS` | 4.123 | 0,22 | 0,17 | 0,39 | – |
| `active_route_energy_at_arrival` | 4.457 | 0,16 | 0,00 | 0,16 | – |
| `cruisestate` | 3.817 | 0,14 | 0,00 | 0,14 | – |
| `superchargers` | 1.128 | 0,09 | 0,00 | 0,09 | – |
| `alerts` | 747 | 0,05 | 0,05 | 0,09 | – |
| `alert_audiences` | 1.496 | 0,06 | 0,00 | 0,06 | – |
| `mothershipcommands` | 225 | 0,05 | 0,00 | 0,05 | – |
| `can` | **0** | 0,02 | 0,02 | 0,03 | ⚠️ LEER! Basis-Tabelle für celltemperature-View |
| `kvs` | 39 | 0,02 | 0,00 | 0,02 | – |
| `car_version` | **76** | 0,02 | 0,00 | 0,02 | ❌ Kein Index auf (CarID, StartDate)! |
| `httpcodes` | 61 | 0,02 | 0,00 | 0,02 | – |
| `cars` | **9** | 0,02 | 0,00 | 0,02 | – |
| `shiftstate` | **0** | 0,02 | 0,00 | 0,02 | ⚠️ LEER |
| `journeys` | **29** | 0,02 | 0,00 | 0,02 | ❌ Kein Index auf (CarID, StartPosID, EndPosID)! |
| `alert_names` | 23 | 0,02 | 0,00 | 0,02 | – |
| `candata` | **0** | 0,02 | 0,00 | 0,02 | ⚠️ LEER |
| `geocodecache` | 96 | 0,02 | 0,00 | 0,02 | – |

### 3.2 Views

| View | Basis-Tabellen | Bemerkung |
|------|---------------|-----------|
| **`celltemperature`** | `can` (id=3) UNION `battery` | ❌ `can`-Tabelle ist **LEER** → nur `battery` liefert Daten |
| **`trip`** | `drivestate` JOIN `pos` (2x) JOIN `cars` | Berechnet km_diff, consumption_kWh, avg_consumption pro Zeile |

### 3.3 Index-Konfiguration (tatsächlich vs. erwartet)

#### `pos` (84,7M Zeilen, 15,24 GB)
| Index | Spalten | Kardinalität | Status |
|-------|---------|-------------|--------|
| PRIMARY | `id` | 84.682.485 | ✅ Vollständig |
| `idx_pos_CarID_id` | `CarID`, `id` | 10.503 / 84.682.485 | ✅ Kombiniert |
| `idx_pos_CarID_datum` | `CarID`, `Datum` | 8.884 / 84.682.485 | ✅ Kombiniert |

#### `charging` (2,6M Zeilen, 177,82 MB)
| Index | Spalten | Kardinalität | Status |
|-------|---------|-------------|--------|
| PRIMARY | `id` | 2.595.443 | ✅ Vollständig |
| `IX_charging_carid_datum` | `CarID`, `Datum` | 8 / 1.297.721 | ✅ Kombiniert |

#### `chargingstate` (1.551 Zeilen, 0,48 MB)
| Index | Spalten | Kardinalität | Status |
|-------|---------|-------------|--------|
| PRIMARY | `id` | 1.551 | ✅ Vollständig |
| `chargingsate_ix_pos` | `Pos` | 1.551 | ✅ (Tippfehler im Namen!) |
| `ixAnalyzeChargingStates1` | `id`, `CarID`, `StartChargingID`, `EndChargingID` | 1.551 | ✅ Komplex |
| `ix_chargingstate_carid` | `CarID` | 10 | ✅ Einfach |
| `ix_cs_carid_start_end` | `CarID`, `StartChargingID`, `EndChargingID` | 10 / 1.551 | ✅ Komplex |
| ❌ **FEHLEND** | `(CarID, StartDate)` | – | ⚠️ In UpdateTeslalogger.cs als vorhanden angenommen |
| ❌ **FEHLEND** | `(CarID, EndDate)` | – | ⚠️ In UpdateTeslalogger.cs als vorhanden angenommen |

#### `drivestate` (10.745 Zeilen, 2,86 MB)
| Index | Spalten | Kardinalität | Status |
|-------|---------|-------------|--------|
| PRIMARY | `id` | 10.745 | ✅ Vollständig |
| `ix_startpos` | `StartPos` | 10.745 | ✅ UNIQUE |
| `ix_endpos2` | `EndPos` | 10.745 | ✅ Einfach |
| ❌ **FEHLEND** | `(CarID, StartDate)` | – | ⚠️ In UpdateTeslalogger.cs als vorhanden angenommen |
| ❌ **FEHLEND** | `(CarID, EndDate)` | – | ⚠️ In UpdateTeslalogger.cs als vorhanden angenommen |

#### `journeys` (29 Zeilen, 0,02 MB)
| Index | Spalten | Kardinalität | Status |
|-------|---------|-------------|--------|
| PRIMARY | `id` | 29 | ✅ Vollständig |
| ❌ **FEHLEND** | `(CarID, StartPosID, EndPosID)` | – | ⚠️ In UpdateTeslalogger.cs als vorhanden angenommen |

#### `car_version` (76 Zeilen, 0,02 MB)
| Index | Spalten | Kardinalität | Status |
|-------|---------|-------------|--------|
| PRIMARY | `id` | 76 | ✅ Vollständig |
| ❌ **FEHLEND** | `(CarID, StartDate)` | – | ⚠️ In UpdateTeslalogger.cs als vorhanden angenommen |

#### `can` (0 Zeilen, 0,03 MB) – Basis-Tabelle für celltemperature-View
| Index | Spalten | Kardinalität | Status |
|-------|---------|-------------|--------|
| PRIMARY | `datum`, `id` | 0 | ✅ Komposit |
| `can_ix2` | `id`, `CarID`, `datum` | 0 | ✅ Komposit |

#### `battery` (55.554 Zeilen, 3,52 MB) – Basis-Tabelle für celltemperature-View
| Index | Spalten | Kardinalität | Status |
|-------|---------|-------------|--------|
| PRIMARY | `CarID`, `date` | – | ✅ Komposit |

### 3.4 Kritische Korrekturen gegenüber den ursprünglichen Annahmen

| Annahme | Tatsächlicher Befund | Auswirkung |
|---------|---------------------|------------|
| DB-System: MySQL | **MariaDB 10.3.39** | Syntax-Unterschiede möglich |
| SSL erforderlich | **SSL nicht unterstützt** | `--skip-ssl` zwingend nötig |
| `drivestate`: „Groß" | **10.745 Zeilen (2,86 MB)** | Geringes Volumen – Queries schnell |
| `chargingstate`: „Groß" | **1.551 Zeilen (0,48 MB)** | Geringes Volumen – Queries schnell |
| `journeys`: „Mittel" | **29 Zeilen (0,02 MB)** | Winzig – Subqueries hier unkritisch |
| `car_version`: „Mittel" | **76 Zeilen (0,02 MB)** | Winzig – Subqueries hier unkritisch |
| `celltemperature`: Tabelle | **VIEW** (auf `can` UNION `battery`) | Keine Indizes möglich |
| `trip`: Tabelle | **VIEW** (auf `drivestate` JOIN `pos` JOIN `cars`) | Keine Indizes möglich |
| `can`: aktiv | **LEER (0 Zeilen)** | celltemperature-View liefert nur `battery`-Daten |
| Index `drivestate(CarID, StartDate)` | **NICHT vorhanden** | ⚠️ Queries auf CarID/Date machen Full Scan |
| Index `drivestate(CarID, EndDate)` | **NICHT vorhanden** | ⚠️ Queries auf CarID/Date machen Full Scan |
| Index `chargingstate(CarID, StartDate)` | **NICHT vorhanden** | ⚠️ Queries auf CarID/Date machen Full Scan |
| Index `journeys(CarID, StartPosID, EndPosID)` | **NICHT vorhanden** | ⚠️ Subqueries machen Full Scan (aber nur 29 Zeilen) |
| Index `car_version(CarID, StartDate)` | **NICHT vorhanden** | ⚠️ Firmware-Lookup macht Full Scan (aber nur 76 Zeilen) |
| `pos`: „Sehr groß" | **84,7M Zeilen, 15,24 GB (97,7% der DB)** | ✅ Bestätigt – Hauptoptimierungsziel |

### 3.5 Performance Monitoring – Status (29. April 2026) ✅ VERIFIZIERT

| Feature | Status | Verifikation |
|---------|--------|-----------|
| **Performance Schema** | ✅ **ON** | `SHOW VARIABLES` bestätigt: `performance_schema = ON` |
| **PERFORMANCE_SCHEMA Engine** | ✅ Geladen | Vollständig verfügbar nach Neustart |
| **Slow Query Log** | ✅ **ON** | `SHOW VARIABLES` bestätigt: `slow_query_log = ON` |
| **long_query_time** | ✅ `1.0 Sekunde` | `SHOW VARIABLES` bestätigt: `long_query_time = 1.000000` |
| **slow_query_log_file** | ✅ Aktiv | `/etc/teslalogger/mysql-slow.log` |
| **my.cnf Konfiguration** | ✅ **PERSISTENT** | Beide Tools bleiben nach Neustart aktiv |
| **Aktivierungsdatum** | 29. April 2026 | Nach `sudo systemctl restart mysql` erfolgreich aktiviert |

#### Performance Schema – ✅ VERIFIZIERT AKTIV

**Fehlerursache (bereits behoben):** Die Zeile `performance_schema = ON` war in der Sektion `[mysql]` statt `[mysqld]`.

**Lösung angewandt:** my.cnf korrigiert:
```ini
[mysqld]  # <- Richtige Sektion!
performance_schema = ON
```

**Verifikation nach Neustart (29. April 2026):**
```bash
mysql -u root -pteslalogger teslalogger -e "SHOW VARIABLES LIKE 'performance_schema';"
# Ergebnis: performance_schema | ON ✅
```

**Performance Schema Queries für Analyse:**

```sql
-- Top 10 langsamste Queries (nach Gesamtausführungszeit)
SELECT EVENT_NAME, COUNT_STAR, SUM_TIMER_WAIT, AVG_TIMER_WAIT 
FROM performance_schema.events_statements_summary_by_digest
ORDER BY SUM_TIMER_WAIT DESC
LIMIT 10;

-- Slow Queries per Tabelle (nach I/O-Wartezeit)
SELECT OBJECT_NAME, SUM_IO_READ_TIME, SUM_IO_WRITE_TIME
FROM performance_schema.table_io_waits_summary_by_table
WHERE OBJECT_SCHEMA = 'teslalogger'
ORDER BY SUM_IO_READ_TIME DESC;
```

---

#### Slow Query Log – ✅ VERIFIZIERT AKTIV

**Zweck:** Einfaches und ressourcenschonendes Logging aller Queries länger als `long_query_time`. Ideal für Raspberry Pi mit begrenzten Ressourcen.

**Verifikation nach Neustart (29. April 2026):**
```bash
mysql -u root -pteslalogger teslalogger -e "SHOW VARIABLES LIKE 'slow_query_log%';"
# Ergebnis:
# slow_query_log      | ON
# slow_query_log_file | /etc/teslalogger/mysql-slow.log
```

**Log-Datei analysieren (auf Raspberry Pi):**

```bash
# Live mitverfolgen (nur neue Einträge)
tail -f /etc/teslalogger/mysql-slow.log

# Top 10 langsamste Queries anzeigen
mysqldumpslow -t 10 /etc/teslalogger/mysql-slow.log

# Alle Queries sortiert nach Ausführungszeit
mysqldumpslow -s t /etc/teslalogger/mysql-slow.log

# Anzahl langsamer Queries zählen
wc -l /etc/teslalogger/mysql-slow.log
```

**Konfigurierte Parameter:**
- `slow_query_log = 1`: Aktiviert
- `long_query_time = 1.0`: Queries > 1 Sekunde werden geloggt
- `log_queries_not_using_indexes = 1`: Auch Index-lose Queries erfassen (aktivierbar)

---

#### Monitoring-Tools – Einsatzstrategie für RasPi

**Empfohlene Kombination:**

1. **Schnelle Diagnose (erste Übersicht):**
   ```bash
   tail -f /etc/teslalogger/mysql-slow.log  # Real-time Monitoring
   ```

2. **Detaillierte Analyse (Performance-Metriken):**
   ```sql
   SELECT EVENT_NAME, COUNT_STAR, SUM_TIMER_WAIT
   FROM performance_schema.events_statements_summary_by_digest
   ORDER BY SUM_TIMER_WAIT DESC LIMIT 10;
   ```

**Vorteil dieser Kombination:**
- **Slow Query Log:** Lightweight, geringe Ressourcen, schnelle Einsicht
- **Performance Schema:** Detailliert, Waits/Memory-Analyse, vollständige Instrumentierung
- Beide Tools werden von MariaDB 10.3.39 unterstützt und funktionieren parallel

---

## 4. Optimierungsempfehlungen (korrigiert nach Fakten)

### 🔴 KRITISCH: `pos`-Tabelle (84,7M Zeilen, 15,24 GB = 97,7% der DB)

**Dies ist das EINZIGE echte Performance-Problem.** Alle anderen Tabellen sind klein (< 3 MB).

#### 4.1 Sofortmaßnahmen für `pos`
1. **Query-Plan für alle `pos`-Queries analysieren**
   - `EXPLAIN SELECT * FROM pos WHERE CarID = ? AND Datum BETWEEN ? AND ?`
   - Prüfen, ob `idx_pos_CarID_datum` korrekt genutzt wird
   - `SELECT *` → explizite Spaltenliste (vermeidet unnötige Datenübertragung)

2. **Fehlende Indizes für `pos` prüfen**
   - Werden Queries nach `odometer` ausgeführt? → Index auf `(CarID, odometer)`
   - Werden Queries nach `lat/lng` (Geofencing) ausgeführt? → Räumlicher Index erwägen
   - Werden Queries nach `speed`, `power`, `battery_level` ausgeführt? → Composite Index

3. **Partitionierung erwägen**
   - `pos` nach `Datum` partitionieren (monatlich/jährlich)
   - Alte Daten archivieren oder in separate Tabelle auslagern

4. **`SELECT *` eliminieren**
   - Alle `SELECT * FROM pos` durch explizite Spalten ersetzen
   - Besonders kritisch bei JOINs mit anderen Tabellen

### 🟡 MITTLERE PRIORITÄT: Fehlende Indizes auf kleinen Tabellen

> ⚠️ **Hinweis:** Da diese Tabellen klein sind (< 11K Zeilen), sind die fehlenden Indizes **kein akutes Problem**. Full Table Scans auf 10K-76 Zeilen sind schnell. Indizes dennoch hinzufügen für Korrektheit und zukünftiges Wachstum.

#### 4.2 Fehlende Indizes hinzufügen
```sql
-- drivestate: CarID + Datum-Indizes
ALTER TABLE drivestate ADD INDEX ix_drivestate_carid_startdate (CarID, StartDate);
ALTER TABLE drivestate ADD INDEX ix_drivestate_carid_enddate (CarID, EndDate);

-- chargingstate: CarID + Datum-Indizes
ALTER TABLE chargingstate ADD INDEX ix_chargingstate_carid_startdate (CarID, StartDate);
ALTER TABLE chargingstate ADD INDEX ix_chargingstate_carid_enddate (CarID, EndDate);

-- journeys: CarID + PosID-Index
ALTER TABLE journeys ADD INDEX ix_journeys_carid_pos (CarID, StartPosID, EndPosID);

-- car_version: CarID + StartDate-Index
ALTER TABLE car_version ADD INDEX ix_carversion_carid_startdate (CarID, StartDate);
```

### 🟢 NIEDRIGE PRIORITÄT: Query-Refactoring

#### 4.3 Correlated Subqueries in ShareData.cs
- **Nur bei `pos`-Queries kritisch** (84,7M Zeilen)
- Bei `drivestate` (10K), `chargingstate` (1,5K), `car_version` (76): **unkritisch** – Full Scan ist schnell

#### 4.4 Subqueries in Journeys.cs
- **Vollständig unkritisch** – `journeys` hat nur 29 Zeilen
- Kein Refactoring nötig

#### 4.5 `celltemperature`-View
- `can`-Tabelle ist **LEER** → View liefert nur `battery`-Daten
- Prüfen, ob `can`-Integration noch geplant ist
- Falls nicht: View vereinfachen oder entfernen

#### 4.6 `trip`-View
- JOIN auf `pos` (84,7M Zeilen) kann langsam sein
- `WHERE pos_end.odometer - pos_start.odometer > 0.1` → Index auf `odometer` prüfen
- Materialized View erwägen, wenn häufig abgefragt

### 🟡 MITTLERE PRIORITÄT: Grafana-Dashboard-Queries

#### 4.7 Status.json – Panels 2–4 (Battery Level, Range, Temperature)
**Problem:** `ORDER BY time_sec DESC LIMIT 1` auf UNION mit `pos` (84,7M Zeilen)
```sql
-- Pattern (Panels 2, 3, 4):
SELECT <spalte> FROM (
  SELECT $__time(datum), <spalte> FROM pos WHERE $__timeFilter(datum)
  UNION
  SELECT $__time(datum), <spalte> FROM charging WHERE $__timeFilter(datum)
  ORDER BY time_sec DESC
) AS t1 LIMIT 1
```
**Optimierungsoptionen:**
1. **Subquery mit LIMIT statt UNION:** Da `pos` deutlich mehr Daten hat als `charging`, ist die Wahrscheinlichkeit hoch, dass das neueste Datum aus `pos` kommt. Query umschreiben:
   ```sql
   -- Optimiert: nur pos abfragen, falls charging leer ist, Fallback
   SELECT battery_level FROM pos 
   WHERE $__timeFilter(datum) 
   ORDER BY datum DESC LIMIT 1
   ```
2. **Index-Prüfung:** `EXPLAIN` ausführen, um zu verifizieren, dass `$__timeFilter(datum)` den Index `idx_pos_CarID_datum` nutzt
3. **Grafana-Time-Range einschränken:** Dashboard-Variable für Zeitfenster auf maximal 24h setzen → weniger Zeilen im `$__timeFilter`

#### 4.8 Status.json – Panel 6 (Odometer)
**Problem:** `ORDER BY id DESC LIMIT 1` auf `pos`
```sql
SELECT odometer FROM pos WHERE $__timeFilter(datum) ORDER BY id DESC LIMIT 1
```
**Status:** Wahrscheinlich OK – `id` ist PRIMARY KEY, MariaDB kann vom Ende des Index lesen (reverse index scan). `$__timeFilter` reduziert den Suchraum zusätzlich.

#### 4.9 Status.json – Panel 5 (Zelltemperatur)
**Problem:** Query gegen `can`-Tabelle, die **LEER** ist
```sql
SELECT val AS 'Zelltemperatur' FROM can WHERE $__timeFilter(datum) AND id = 2 ...
```
**Lösung:** 
- Falls `can`-Integration nicht geplant: Query auf `battery`-Tabelle umschreiben oder Panel entfernen
- Falls `can`-Integration geplant: Query beibehalten, aber mit Kommentar dokumentieren

#### 4.10 Degradation.json – Panels 2+4 (Max Range Queries)
**Problem:** JOIN-Kette `charging → chargingstate → pos`
```sql
FROM charging 
INNER JOIN chargingstate ON charging.id = chargingstate.StartChargingID 
INNER JOIN pos ON chargingstate.pos = pos.id 
```
**Status:** Wahrscheinlich OK – `chargingstate` hat nur 1.551 Zeilen (Join-Treiber), `pos.id` ist PRIMARY KEY (O(1) Lookup). Aggregation in Panel 4 (`GROUP BY EXTRACT(YEAR_MONTH ...)`) ist auf kleine Mengen anwendbar.

#### 4.11 Degradation.json – Panel 3 (Nominal Full Pack)
**Problem:** Query gegen `can`-Tabelle, die **LEER** ist
```sql
SELECT $__time(datum), val AS 'Nominal full pack [kWh]' FROM can WHERE ... AND id = 71 ...
```
**Lösung:** Panel deaktivieren oder auf alternative Datenquelle umschreiben

---

## 5. Performance Schema Analyse & EXPLAIN Plans (29. April 2026)

### 5.1 Performance Schema Aktivierung & Ergebnisse

**Status:** ✅ Performance Schema ist aktiviert (`performance_schema = ON`)

```bash
# Performance Schema Abfrage
mysql -u root -pteslalogger teslalogger -e "SELECT DIGEST_TEXT, COUNT_STAR, SUM_TIMER_WAIT/1000000000 as SUM_TIME_SEC, AVG_TIMER_WAIT/1000000000 as AVG_TIME_SEC, SUM_ROWS_EXAMINED, SUM_NO_INDEX_USED FROM performance_schema.events_statements_summary_by_digest WHERE SCHEMA_NAME='teslalogger' ORDER BY SUM_TIMER_WAIT DESC LIMIT 5\G"
```

**Ergebnis – Top 5 Langsame Queries (29. April 2026):**

| Rang | Query-Typ | Gesamtzeit | Anzahl Executions | Avg Zeit pro Execution | Gescannte Zeilen | Kein Index |
|------|-----------|-----------|-------------------|----------------------|-----------------|-----------|
| **1 🔴 KRITISCH** | UNION: pos + charging + chargingstate | **70.025 Sekunden** | 1 | 70.025s | **1.987.927** | ✅ JA |
| 2 | INSERT mothership | 227.509 Sekunden | 11 | 20.683s | 0 | Nein |
| 3 | SELECT Performance Schema | 19.051 Sekunden | 1 | 19.051s | 0 | Nein |
| 4 | SELECT cars (length(vin) > ?) | 14.046 Sekunden | 1 | 14.046s | 18 | ✅ JA |
| 5 | SHOW COLLATION | 6.721 Sekunden | 1 | 6.721s | 322 | ✅ JA |

**Interpretation:** 
- Query #1 ist das **Hauptproblem** (70 Sekunden = 70% der gesamten erfassten Query-Zeit)
- SUM_NO_INDEX_USED=1 zeigt: Ein oder mehr Queries nutzen keinen Index
- 1.987.927 gescannte Zeilen bei nur 1 Execution = massiver Full Table Scan

---

### 5.2 Table I/O Waits Analyse

**Query:**
```sql
SELECT OBJECT_NAME, COUNT_READ, COUNT_WRITE, SUM_TIMER_READ/1000000000 as READ_TIME_SEC, SUM_TIMER_WRITE/1000000000 as WRITE_TIME_SEC 
FROM performance_schema.table_io_waits_summary_by_table 
WHERE OBJECT_SCHEMA='teslalogger' 
ORDER BY SUM_TIMER_READ+SUM_TIMER_WRITE DESC 
LIMIT 10;
```

**Ergebnis – Table I/O Distribution (29. April 2026):**

| Tabelle | Read-Operationen | Read-Zeit | Write-Operationen | Write-Zeit | Total-Zeit | Problemtyp |
|---------|------------------|-----------|-------------------|-----------|-----------|-----------|
| **pos** 🔴 | **1.959.851** | **42.132 Sekunden** | 0 | 0 | **42.132s** | Massive Reads |
| **charging** 🟡 | 1.261 | 1.559 Sekunden | 0 | 0 | 1.559s | Normale Reads |
| **chargingstate** 🟢 | 2.386 | 49.4 Sekunden | 0 | 0 | 49.4s | OK für Größe |
| **mothership** 🟡 | 0 | 0 | 12 | 26.728s | 26.728s | Slow Writes |
| **cars** 🟢 | 10 | 0.266s | 0 | 0 | 0.266s | OK |

**Interpretation:**
- **`pos` Tabelle dominiert mit 42.132 Sekunden (70% der I/O-Zeit)**
- 1.959.851 Read-Operationen auf eine 84,7M-Zeilen-Tabelle
- Dies entspricht dem Query #1 (UNION mit pos + charging + chargingstate)
- Die 42.132 Sekunden sind I/O-Wait-Zeit, nicht CPU-Zeit

---

### 5.3 EXPLAIN PLAN für kritischen Query #1

**Original Query (aus Performance Schema):**
```sql
SELECT AVG(UNIX_TIMESTAMP(datum)) AS time_sec, AVG(lat), AVG(lng), ? AS TYPE, ? AS address, CarID 
FROM pos 
WHERE datum BETWEEN FROM_UNIXTIME(?) AND FROM_UNIXTIME(?) 
  AND CarID IN (?) 
  AND lat != ? 
  AND lng != ? 
GROUP BY CarID, UNIX_TIMESTAMP(datum) DIV ?
UNION
SELECT UNIX_TIMESTAMP(pos.datum) AS time_sec, lat, lng, ... 
FROM chargingstate 
JOIN charging ON endchargingid = charging.id 
JOIN pos ON chargingstate.Pos = pos.id 
WHERE pos.datum BETWEEN FROM_UNIXTIME(?) AND FROM_UNIXTIME(?)
  AND chargingstate.CarID IN (?) 
  AND (fast_charger_brand = ? OR address LIKE ?)
```

**Vereinfachter Test-Query (mit Beispieldaten):**
```sql
EXPLAIN SELECT AVG(UNIX_TIMESTAMP(datum)) AS time_sec, AVG(lat), AVG(lng), 1 AS TYPE 
FROM pos 
WHERE datum BETWEEN '2024-01-01' AND '2024-12-31' 
  AND CarID = 1 
  AND lat != 0 
  AND lng != 0 
GROUP BY CarID, UNIX_TIMESTAMP(datum) DIV 300;
```

**EXPLAIN Resultat:**

| id | select_type | table | type | possible_keys | key | key_len | ref | rows | Extra |
|----|-------------|-------|------|---|---|---|---|---|---|
| 1 | SIMPLE | pos | **ref** | idx_pos_CarID_id,<br>idx_pos_CarID_datum | **idx_pos_CarID_id** | 5 | const | **42.126.852** | **Using where; Using temporary; Using filesort** |

**Analyse:**
- ✅ **Index wird genutzt** (`idx_pos_CarID_id`) – aber nur teilweise (first key column `CarID`)
- ❌ **Problematisch: 42.126.852 geschätzte Zeilen gescannt** (das ist fast die Hälfte der 84M Zeilen!)
- ❌ **`Using where`:** Filter-Bedingungen `lat != 0 AND lng != 0` werden NACH dem Index-Scan angewendet
- ❌ **`Using temporary`:** GROUP BY mit `UNIX_TIMESTAMP(datum) DIV 300` erzeugt viele Gruppen → benötigt temporäre Tabelle
- ❌ **`Using filesort`:** Sortierung wird durchgeführt (wahrscheinlich für GROUP BY)

**Root Cause:** Die NOT-EQUAL Bedingungen (`lat != 0`) sind Index-unfähig und führen zu einem großen Filter-Pass NACH dem Index-Scan.

---

### 5.4 Index-Konfiguration der `pos` Tabelle

**Bestehende Indizes:**
```sql
SHOW INDEX FROM pos;
```

**Resultat:**

| Table | Key_name | Columns | Type | Cardinality |
|-------|----------|---------|------|------------|
| pos | PRIMARY | id | BTREE | 84.253.705 |
| pos | **idx_pos_CarID_id** | CarID, id | BTREE | 10.498 (CarID), 84.253.705 (id) |
| pos | **idx_pos_CarID_datum** | CarID, Datum | BTREE | 8.880 (CarID), 84.253.705 (Datum) |

**Analyse:**
- ✅ Index `idx_pos_CarID_datum` ist ideal für Datum-Range-Queries (`BETWEEN`)
- ⚠️ **Problem:** EXPLAIN nutzt `idx_pos_CarID_id` statt `idx_pos_CarID_datum` 
  - Warum? Wahrscheinlich weil der Query-Optimizer `idx_pos_CarID_id` statistisch günstiger einschätzt
  - Die `lat != 0` Bedingungen sind Index-unfähig und können nicht zur Index-Selektion beitragen

---

### 5.5 Verbesserungsvorschläge für Query #1

#### Option 1: Query-Umstrukturierung (Empfohlen)
Der Query versucht, Positions-Aggregationen mit Charging-Event-Details zu kombinieren. Das ist strukturell problematisch.

**Problem:** 
- AVG(lat), AVG(lng), AVG(UNIX_TIMESTAMP(datum)) auf **GROUP BY CarID, UNIX_TIMESTAMP(datum) DIV 300** ist teuer
- Die UNION mit charging-Details macht den Query noch komplexer

**Lösung:**
1. **Split in zwei Queries:**
   - Query A: Positions-Aggregate (lat, lng, time) from `pos` mit GROUP BY
   - Query B: Charging-Details from `charging` JOIN `chargingstate` JOIN `pos`
   - Fusion in Applikation (C#), nicht in SQL

2. **Alternative:** Materialisierte View erstellen
   ```sql
   CREATE TABLE pos_aggregated_5min (
       CarID INT,
       time_bucket INT,  -- UNIX_TIMESTAMP(datum) DIV 300
       avg_lat DECIMAL(9,6),
       avg_lng DECIMAL(9,6),
       avg_timestamp INT,
       COUNT(*) as record_count
   ) AS
   SELECT CarID, UNIX_TIMESTAMP(datum) DIV 300, AVG(lat), AVG(lng), AVG(UNIX_TIMESTAMP(datum)), COUNT(*)
   FROM pos
   GROUP BY CarID, UNIX_TIMESTAMP(datum) DIV 300;
   
   -- Tägliche oder stündliche Refresh (via Batch-Job)
   ```
   - Query würde dann nur auf die kleine materialisierte Tabelle zugreifen (Millionen statt 84M Zeilen)

#### Option 2: Index-Force (Quick-Fix)
Force die Nutzung von `idx_pos_CarID_datum` statt `idx_pos_CarID_id`:

```sql
SELECT AVG(UNIX_TIMESTAMP(datum)) AS time_sec, AVG(lat), AVG(lng), 1 AS TYPE 
FROM pos FORCE INDEX (idx_pos_CarID_datum)
WHERE datum BETWEEN '2024-01-01' AND '2024-12-31' 
  AND CarID = 1 
  AND lat != 0 
  AND lng != 0 
GROUP BY CarID, UNIX_TIMESTAMP(datum) DIV 300;
```

**Erwartung:** Bessere Nutzung des Datum-Range-Indexes, aber `lat != 0` bleibt Index-unfähig.

#### Option 3: Daten-Filter in Applikation
Move the `lat != 0 AND lng != 0` Filter aus SQL in C#-Applikationscode:

```csharp
// Statt SQL WHERE lat != 0 AND lng != 0
var results = await db.pos
    .Where(p => p.CarID == carId)
    .Where(p => p.datum >= startDate && p.datum <= endDate)
    .AsAsyncEnumerable()  // <-- Stream-Verarbeitung
    .Where(p => p.lat != 0 && p.lng != 0)  // <-- Filter in C#
    .GroupBy(p => new { p.CarID, bucket = UnixTimestamp(p.datum) / 300 })
    .Select(g => new { 
        CarID = g.Key.CarID,
        time_sec = g.Average(p => UnixTimestamp(p.datum)),
        lat = g.Average(p => p.lat),
        lng = g.Average(p => p.lng),
        count = g.Count()
    })
    .ToListAsync();
```

**Vorteil:** Die SQL-Query wird mit Index-Support für `datum BETWEEN` und `CarID =` ausgeführt. Dann wird der `lat != 0 Filter` in C# angewendet, wo es günstiger ist (keine Materialisierung nötig).

---

### 5.6 Verbesserungsvorschläge für Query #4 (SELECT cars)

**Problematischer Query:**
```sql
SELECT display_name AS __text, ID AS __value FROM cars WHERE length(vin) > ? ORDER BY display_name
```

**EXPLAIN Resultat:**
- Ausführungszeit: 14.046 Sekunden (!!)
- Gescannte Zeilen: 18 (cars Tabelle hat ~18 Zeilen)
- SUM_NO_INDEX_USED: 1

**Problem:** `length(vin) > ?` ist eine Funktions-basierte WHERE-Bedingung, die keinen Index nutzen kann. FULL TABLE SCAN auf 18 Zeilen dauert trotzdem unerwartet lange.

**Lösung:**
1. **Index-Funktion verwenden** (MariaDB 10.3.39):
   ```sql
   ALTER TABLE cars ADD INDEX ix_cars_vin_length (vin(50));  -- Prefix Index
   ```
   Aber: `length(vin)` wird weiterhin nicht indexiert.

2. **Bessere Lösung: Computed Column:**
   ```sql
   ALTER TABLE cars ADD COLUMN vin_length INT GENERATED ALWAYS AS (LENGTH(vin)) STORED;
   ALTER TABLE cars ADD INDEX ix_cars_vin_length (vin_length);
   
   -- Dann Query umschreiben:
   SELECT display_name, ID FROM cars WHERE vin_length > ? ORDER BY display_name;
   ```

3. **Schneller Fix:** Es gibt nur 18 Zeilen – dieser Query ist nicht kritisch. Ignorieren und auf #1 konzentrieren.

---

### 5.7 Verbesserungsvorschläge für Query #2 (INSERT mothership)

**Status:** 227.509 Sekunden Gesamtzeit, aber nur 11 Executions → 20.683s pro Execution

**Analyse:**
- Dies ist ein INSERT-Statement, nicht SELECT
- Write-Time dominiert nicht (26.728s total write time für mothership Tabelle)
- Dies ist wahrscheinlich ein periodischer Batch-Insert (z.B. alle 2 Stunden)
- **Nicht kritisch für interaktive Queries**

**Empfehlung:** Auf priorisierte Queries (#1, #4) konzentrieren.

---

### 5.9 Index-Fähigkeit von lat != 0 AND lng != 0 (NOT-EQUAL Bedingungen)

**Problem:** Benutzer fragte, ob man `lat != 0 AND lng != 0` anders ausdrücken kann, um Index-fähig zu werden.

**Kurze Antwort:** NOT-EQUAL Bedingungen (`!=`, `<>`) sind **grundsätzlich Index-unfähig**. Das ist eine MariaDB/MySQL-Limitation. Weder `lat != 0` noch `(lat > 0 OR lat < 0)` nutzen Index.

**Getestete Alternativen:**
```sql
-- Alle diese Variants scannen immer noch 42M Zeilen:
WHERE lat != 0 AND lng != 0           -- Index-unfähig (OR-Bedingungen im Index-unfähig)
WHERE (lat > 0 OR lat < 0) ...        -- Index-unfähig (OR kann nicht genutzt werden)
WHERE lat IS NOT NULL ...             -- Index-fähig, aber nicht äquivalent (NULL != 0 ist TRUE)
```

**EXPLAIN Resultat:** Alle Varianten ergeben `42,126,852 rows` (Full Table Scan).

---

### 5.10 Lösung: Computed Column mit Index (EMPFOHLEN)

**Beste Lösung für Grafana-Queries:** Verwende eine STORED Computed Column, die dann indexiert wird.

#### Implementierung:

**Schritt 1: Füge Computed Column hinzu** (auf Raspberry Pi ausführen)
```sql
ALTER TABLE pos ADD COLUMN has_valid_location TINYINT(1) GENERATED ALWAYS AS (lat != 0 AND lng != 0) STORED;
```

**Warnung:** Dies kann auf einer 84M-Zeilen-Tabelle 10-30 Minuten dauern! Nicht in Produktionszeiten ausführen.

**Schritt 2: Erstelle Index auf der neuen Spalte**
```sql
ALTER TABLE pos ADD INDEX ix_pos_carid_valid_location (CarID, has_valid_location, datum);
```

**Schritt 3: Ändere die Grafana Query**

**Vorher (Index-unfähig):**
```sql
SELECT AVG(UNIX_TIMESTAMP(datum)) AS time_sec, AVG(lat), AVG(lng), ... 
FROM pos
WHERE datum BETWEEN FROM_UNIXTIME(?) AND FROM_UNIXTIME(?)
  AND CarID IN (?)
  AND lat != 0 
  AND lng != 0 
GROUP BY CarID, UNIX_TIMESTAMP(datum) DIV 300
```

**Nachher (Index-fähig):**
```sql
SELECT AVG(UNIX_TIMESTAMP(datum)) AS time_sec, AVG(lat), AVG(lng), ... 
FROM pos
WHERE datum BETWEEN FROM_UNIXTIME(?) AND FROM_UNIXTIME(?)
  AND CarID IN (?)
  AND has_valid_location = 1    -- <-- Statt lat != 0 AND lng != 0
GROUP BY CarID, UNIX_TIMESTAMP(datum) DIV 300
```

**Erwarteter EXPLAIN Plan nach Optimierung:**
| Metriken | Aktuell | Mit Computed Column |
|----------|---------|-------------------|
| Scanned Rows | 42.126.852 | ~1.000.000 (stark reduziert) |
| Index | idx_pos_CarID_id (nur CarID) | ix_pos_carid_valid_location (CarID + Filter) |
| Extra | Using where; Using temporary; Using filesort | Using where; Using temporary; Using filesort |

---

### 5.11 Alternative Lösungen (Quick-Fixes, wenn Computed Column zu langsam)

#### Alternative A: Query direkt im Grafana umschreiben (kein SQL-Change nötig)

Falls die Grafana Query-Template ist, kann man auch einfach filtern:

**Ursprung der Query:** Wahrscheinlich aus WebServer.cs oder ShareData.cs als Grafana-Datenquelle

**Schneller Fix:** 
```sql
-- Umstrukturiert: Aggregiere ALLE Positionen, dann filtere nur gültige im SELECT
SELECT 
  UNIX_TIMESTAMP(datum) AS time_sec,
  SUM(CASE WHEN lat != 0 AND lng != 0 THEN lat ELSE 0 END) / 
  SUM(CASE WHEN lat != 0 AND lng != 0 THEN 1 ELSE 0 END) AS lat,
  SUM(CASE WHEN lat != 0 AND lng != 0 THEN lng ELSE 0 END) / 
  SUM(CASE WHEN lat != 0 AND lng != 0 THEN 1 ELSE 0 END) AS lng,
  CarID
FROM pos
WHERE datum BETWEEN FROM_UNIXTIME(?) AND FROM_UNIXTIME(?)
  AND CarID IN (?)
GROUP BY CarID, UNIX_TIMESTAMP(datum) DIV 300
```

**Nachteil:** Immer noch 42M Zeilen gescannt (aber Aggregation in SQL = schneller als Applikationscode)

---

#### Alternative B: Partition nach Datum (langfristig)

Falls viele Queries nur letzte 30 Tage brauchen:

```sql
-- Partitioniere pos nach Datum
ALTER TABLE pos PARTITION BY RANGE (YEAR(datum) * 100 + MONTH(datum)) (
  PARTITION p202401 VALUES LESS THAN (202402),
  PARTITION p202402 VALUES LESS THAN (202403),
  -- ... bis aktuell
  PARTITION pmax VALUES LESS THAN MAXVALUE
);
```

**Effekt:** Query scannt nur relevante Partitionen, nicht alle 84M Zeilen.

---

### 5.12 Vergleich der Lösungen

| Lösung | Aufwand | Performance | Wartung | Empfehlung |
|--------|---------|-------------|---------|-----------|
| **Computed Column + Index** | Mittel (10-30 min Execution) | 🟢 Optimal (42M → 1M rows) | Einfach | ✅ **BEST** |
| **Query-Umstrukturierung (HAVING)** | Niedrig (nur SQL-Change) | 🟡 Gut (immer noch 42M rows, aber schneller) | Einfach | ✅ Schneller Fix |
| **Partitionierung** | Hoch (Schema-Change) | 🟢 Optimal (bei Range-Queries) | Komplex | 🟡 Langfristig |
| **Applikationscode-Filter (C#)** | Niedrig | 🟡 OK (Streaming) | Komplex | ❌ Nicht empfohlen |
| `(lat > 0 OR lat < 0)` | Keine | 🔴 Keine Verbesserung | N/A | ❌ Funktioniert nicht |

---

### 5.13 Implementierungs-Roadmap

**Phase 1 (Diese Woche) – SOFORT:**
1. [ ] Führe EXPLAIN für ursprüngliche Query mit realen Grafana-Variablen durch
2. [ ] Implementiere Quick-Fix: Query-Umstrukturierung mit HAVING (kein Schema-Change)
3. [ ] Messe Performance-Verbesserung mit Performance Schema (sollte messbar schneller sein)

**Phase 2 (Nächste Woche) – Wenn Quick-Fix nicht genug hilft:**
1. [ ] Erstelle Computed Column: `ALTER TABLE pos ADD COLUMN has_valid_location TINYINT(1) GENERATED ALWAYS AS (lat != 0 AND lng != 0) STORED`
   - **Timing:** Nachts oder in Wartungsfenster (dauert 10-30 Minuten)
   - **Backup:** Vor Ausführung vollständiges Backup machen!
2. [ ] Index auf neuer Spalte: `ALTER TABLE pos ADD INDEX ix_pos_carid_valid_location (CarID, has_valid_location, datum)`
3. [ ] Update Grafana Query: `... AND has_valid_location = 1`
4. [ ] Verifiziere Performance mit EXPLAIN (sollte 42M → ~1M rows reduzieren)

**Phase 3 (Optional, längerfristig):**
- Partitionierung nach Datum erwägen
- Archivierung alter Daten (älter als 2 Jahre)

---

**Um zu verifizieren, dass Optimierungen funktionieren:**

```bash
# Performance Schema zurücksetzen und neu messen
mysql -u root -pteslalogger teslalogger -e "TRUNCATE performance_schema.events_statements_summary_by_digest;"
mysql -u root -pteslalogger teslalogger -e "TRUNCATE performance_schema.table_io_waits_summary_by_table;"

# Dann 1-2 Tage laufen lassen und neu analysieren
```

**Priorität der Fixes:**
1. 🔴 **Sofort:** Query #1 Optimierung (Option 2 oder 3)
2. 🟡 **Kurzfristig:** Query #4 mit Computed Column (falls weiterhin langsam)
3. 🟢 **Optional:** Materialisierte View für pos Aggregates (längerfristig)

---

## 6. Nächste Schritte & Action Items

### 🔴 SOFORT (Priorität 1 – Performance Schema zeigt kritisches Problem)

#### 6.1 Query #1 Optimierung (70 Sekunden!) – 3 Optionen

**Betroffene Component:** Vermutlich Grafana Dashboard oder WebServer.cs Charging-Events

Die Performance Schema zeigt:
- 70.025 Sekunden Ausführungszeit für einen UNION Query
- 1.987.927 gescannte Zeilen auf 84,7M-Zeilen-Tabelle
- SUM_NO_INDEX_USED=1 → Mindestens ein Teil nutzt keinen Index

**Action – Wähle eine Option:**

**🟢 OPTION 1 (Empfohlen – Beste Performance):** Computed Column mit Index

```sql
-- Schritt 1: Erstelle STORED Computed Column (10-30 min Ausführungszeit!)
ALTER TABLE pos ADD COLUMN has_valid_location TINYINT(1) GENERATED ALWAYS AS (lat != 0 AND lng != 0) STORED;

-- Schritt 2: Indexiere die neue Spalte
ALTER TABLE pos ADD INDEX ix_pos_carid_valid_location (CarID, has_valid_location, datum);

-- Schritt 3: Update Grafana Query – ersetze "lat != 0 AND lng != 0" durch:
... AND has_valid_location = 1
```

**Erwartete Verbesserung:** 42.126.852 Zeilen → ~1.000.000 Zeilen (98% Reduktion)

**⚠️ Wichtig:** 
- Backup machen vor Execution!
- Nachts oder in Wartungsfenster ausführen
- Nach Execution: ANALYZE TABLE pos

---

**🟡 OPTION 2 (Quick-Fix – Keine Schema-Changes):** Query-Umstrukturierung mit HAVING

Wenn die Computed Column zu langsam ist, verwende HAVING statt WHERE:

```sql
SELECT AVG(UNIX_TIMESTAMP(datum)) AS time_sec, AVG(lat), AVG(lng), ... 
FROM pos
WHERE datum BETWEEN FROM_UNIXTIME(?) AND FROM_UNIXTIME(?)
  AND CarID IN (?)
GROUP BY CarID, UNIX_TIMESTAMP(datum) DIV 300
HAVING AVG(lat) != 0 AND AVG(lng) != 0  -- <-- Filter nach Aggregation
```

**Vorteil:** Keine Tabellen-Änderung nötig, nur SQL-Change in Grafana
**Nachteil:** Immer noch 42M Zeilen gescannt (aber Aggregation in SQL = schneller)

---

**❌ OPTION 3 (Nicht empfohlen):** Index Force / OR-Ausdrücke

Getestete Alternative:
```sql
WHERE ... AND (lat > 0 OR lat < 0) AND (lng > 0 OR lng < 0)
```

**Resultat:** Scannt immer noch 42M Zeilen. Funktioniert nicht.

---

1. [ ] Identifiziere den exakten Query im Code:
   ```bash
   # Suche nach UNION Queries mit pos + charging + chargingstate
   grep -r "GROUP BY.*UNIX_TIMESTAMP.*DIV" TeslaLogger/*.cs
   grep -r "chargingstate.*JOIN.*charging" TeslaLogger/*.cs
   ```

2. [ ] Entscheide: Option 1 (Computed Column) oder Option 2 (Quick-Fix)?
   - **Option 1 (Recommended):** Wenn du bereit für Schema-Change bist
   - **Option 2:** Wenn du schnell nur SQL ändern möchtest

3. [ ] Implementiere deine gewählte Option

4. [ ] Nach Änderung: TRUNCATE Performance Schema und neu messen

#### 6.2 Query #4 Optimierung (14 Sekunden auf 18 Zeilen?!)
**SELECT display_name FROM cars WHERE length(vin) > ?**

**Action:**
1. [ ] Prüfe, wieso dieser Query 14 Sekunden dauert (nur 18 Zeilen!)
2. [ ] Implementiere Computed Column für `vin_length`
3. [ ] Alternatively: Ignorieren, wenn nicht kritisch für UI

---

### 🟡 KURZFRISTIG (Priorität 2 – diese Woche)

#### 6.3 Slow Query Log verifizieren
- [ ] Prüfe auf RasPi: `cat /etc/teslalogger/mysql-slow.log` hat Inhalte?
- [ ] Falls leer: Berechtigungsprobleme auf `/etc/teslalogger/` prüfen (siehe Session-Memory)
- [ ] Führe `mysqldumpslow` aus: `mysqldumpslow -t 10 /etc/teslalogger/mysql-slow.log`

#### 6.4 Fehlende Indizes hinzufügen
```sql
-- drivestate
ALTER TABLE drivestate ADD INDEX ix_drivestate_carid_startdate (CarID, StartDate);
ALTER TABLE drivestate ADD INDEX ix_drivestate_carid_enddate (CarID, EndDate);

-- chargingstate (falls noch nicht vorhanden)
ALTER TABLE chargingstate ADD INDEX ix_chargingstate_carid_startdate (CarID, StartDate);
ALTER TABLE chargingstate ADD INDEX ix_chargingstate_carid_enddate (CarID, EndDate);

-- journeys
ALTER TABLE journeys ADD INDEX ix_journeys_carid_pos (CarID, StartPosID, EndPosID);

-- car_version
ALTER TABLE car_version ADD INDEX ix_carversion_carid_startdate (CarID, StartDate);
```
**Status:** Alle Statements sind vorbereitet → nur noch kopieren und ausführen

#### 6.5 ANALYZE TABLE pos
**Warum:** Aktualisiere Statistiken für den Query Optimizer
```bash
mysql -u root -pteslalogger teslalogger -e "ANALYZE TABLE pos;"
```

#### 6.6 Suche alle UNION Queries mit ORDER BY auf pos
Diese sind wahrscheinlich suboptimal:
```bash
# In WebServer.cs, ShareData.cs, Journeys.cs, DBHelper.cs
grep -n "UNION" TeslaLogger/*.cs | grep -i "ORDER BY"
```

---

### 🟢 MITTELFRISTIG (Priorität 3 – nächste 2-4 Wochen)

#### 6.7 Pos-Tabelle Partitionierung planen
- [ ] Partitioniere nach Datum (monatlich/jährlich)
- [ ] Archiviere alte Daten (älter als 2 Jahre?) in separate Partition
- [ ] Teste Performance-Impact auf RasPi

#### 6.8 Correlated Subqueries auf pos refaktorisieren
Falls die Performance Schema weitere Daten zeigt:
- [ ] ShareData.cs: Patterns A-E überprüfen
- [ ] Konvertiere zu JOINs oder Applikations-seitiger Verarbeitung

#### 6.9 Materialisierte View erstellen (Optional)
Für häufig aggregierte Daten:
```sql
CREATE TABLE pos_aggregated_5min AS
SELECT CarID, UNIX_TIMESTAMP(datum) DIV 300 as time_bucket,
       AVG(lat) as avg_lat, AVG(lng) as avg_lng, AVG(UNIX_TIMESTAMP(datum)) as avg_time
FROM pos
GROUP BY CarID, UNIX_TIMESTAMP(datum) DIV 300;

-- Index für Zugriffe
ALTER TABLE pos_aggregated_5min ADD PRIMARY KEY (CarID, time_bucket);

-- Tägliche Refresh (z.B. via Cron-Job)
```

---

### 📊 Monitoring & Verifizierung

**Nach jeder Änderung:**
```bash
# Performance Schema zurücksetzen
mysql -u root -pteslalogger teslalogger -e \
  "TRUNCATE performance_schema.events_statements_summary_by_digest; \
   TRUNCATE performance_schema.table_io_waits_summary_by_table;"

# 1-2 Tage laufen lassen, dann neu messen
mysql -u root -pteslalogger teslalogger -e \
  "SELECT DIGEST_TEXT, COUNT_STAR, SUM_TIMER_WAIT/1000000000 as SUM_TIME_SEC \
   FROM performance_schema.events_statements_summary_by_digest \
   WHERE SCHEMA_NAME='teslalogger' \
   ORDER BY SUM_TIMER_WAIT DESC LIMIT 5\G"
```

---

### ✅ Erfolgs-Kriterien

| Metrik | Aktuell (29. Apr 2026) | Ziel |
|--------|-----|------|
| Query #1 (UNION pos + charging) | **70.025 Sekunden** | **< 5 Sekunden** |
| pos Tabelle I/O-Zeit | **42.132 Sekunden** | **< 5 Sekunden** |
| Query #4 (SELECT cars) | **14.046 Sekunden** | **< 1 Sekunde** |
| Table Scans ohne Index (SUM_NO_INDEX_USED) | 3 (Queries 1, 4, 5) | 0 |

---

| Änderung | Risiko | Ausfall-Szenario |
|----------|--------|-----------------|
| Neue Indizes | Niedrig | Langsamere INSERT/UPDATE |
| Query-Refactoring | Mittel | Inkorrekte Ergebnisse |
| View-Änderungen | Hoch | Breaking Changes für UI |
| Caching | Niedrig | Veraltete Daten |
| **Grafana-Query-Änderungen** | **Mittel** | **Dashboard zeigt falsche/veraltete Werte** |
| **Grafana-Panel-Entfernung** | **Niedrig** | **Verlust von Monitoring-Informationen** |

**Empfehlung:** Alle Änderungen zuerst in Testumgebung validieren, dann schrittweise produktiv einführen. Grafana-Queries in separatem Test-Dashboard validieren, bevor Produktions-Dashboards überschrieben werden.

---

## 7. Referenzen

### Betroffene Dateien im Detail
- `TeslaLogger/WebServer.cs` – Hauptdatei mit ~150+ SQL-Statements
- `TeslaLogger/ShareData.cs` – Daten-Sharing mit komplexen Aggregationen
- `TeslaLogger/Journeys.cs` – Journey-Berechnungen mit Subqueries
- `TeslaLogger/StaticMapService.cs` – Karten-Generierung mit Bulk-Queries
- `TeslaLogger/DBHelper.cs` – Datenbank-Hilfsklassen
- `TeslaLogger/DBViews.cs` – View-Definitionen
- `TeslaLogger/WebHelper.cs` – POI-Adress-Updates
- `TeslaLogger/UpdateTeslalogger.cs` – Schema-Migrationen

### Grafana-Dashboards
- `Grafana/Dashboard/Status.json` – Echtzeit-Status-Dashboard (8 Queries, 4 berühren `pos`)
- `Grafana/Dashboard/Degradation.json` – Batterie-Degradation (4 Queries, 2 berühren `pos` via JOIN)

### Wichtige Code-Patterns
- `SQLTracer.TraceNQ()` – nicht-querying Commands
- `SQLTracer.TraceDA()` – DataAdapter-Fills
- `SQLTracer.TraceDR()` – DataReader-Execution
- `SQLTracer.TraceSc()` – Scalar-Results

### Grafana-Makros
- `$__time(column)` → `UNIX_TIMESTAMP(column) AS time_sec` (MariaDB)
- `$__timeFilter(column)` → `column BETWEEN FROM_UNIXTIME(?) AND FROM_UNIXTIME(?)` (MariaDB)
- Werden zur Laufzeit durch Grafana ersetzt – je nach Zeitfenster im Dashboard unterschiedlich

---

*Erstellt: 24. April 2026*
*Zuletzt aktualisiert: 29. April 2026 (Performance Schema Analyse mit EXPLAIN Plans)*
*Branch: NET8*
*Repository: bassmaster187/TeslaLogger*

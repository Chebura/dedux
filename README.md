# About
Dedux - file deduplication utility.

# What it does?

1. Dedux scans file system at specified path and recalculates thumbprints of files very fast;
2. Dedux detects duplicates in report;
3. Dedux caches scanned thumbprints for futher scanning without recalculating hashes, that increase speed of re-scanning file system; 
4. Dedux compares files in input directory with existing files in target directory and deletes files in source if they are exist in target.

# Usage
## CLI
Single call CLI.

## Scheduler
You can use dedux in scheduler to recalculate target file system continuously.

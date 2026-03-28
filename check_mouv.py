import sqlite3
import sys

def check_table(db_path):
    try:
        conn = sqlite3.connect(db_path)
        c = conn.cursor()
        c.execute("SELECT name FROM sqlite_master WHERE type='table' AND name='MouvementsStock';")
        res = c.fetchone()
        conn.close()
        return "FOUND" if res else "MISSING"
    except Exception as e:
        return f"ERROR: {e}"

if __name__ == "__main__":
    if len(sys.argv) > 1:
        print(check_table(sys.argv[1]))

import sys, json, requests

path = r"C:\zcode_build\stripe\dist\IVAN-CENTER.exe"
name = "IVAN-CENTER.exe"

r = requests.get("https://api.gofile.io/servers", timeout=30)
servers = [s["name"] for s in r.json()["data"]["servers"]]
print("servers:", servers)

last = None
for srv in servers:
    url = "https://%s.gofile.io/contents/uploadfile" % srv
    try:
        with open(path, "rb") as f:
            resp = requests.post(url, files={"file": (name, f, "application/octet-stream")}, timeout=300)
        print(srv, "http", resp.status_code)
        print(resp.text[:500])
        if resp.status_code == 200:
            d = resp.json()
            if d.get("status") == "ok":
                print("DOWNLOAD:", d["data"].get("downloadPage"))
                print("DIRECT:", d["data"].get("directLink") or d["data"].get("link"))
                sys.exit(0)
        last = resp.text[:300]
    except Exception as e:
        print(srv, "ERR", type(e).__name__, str(e)[:160])
print("all failed; last:", last)
sys.exit(1)

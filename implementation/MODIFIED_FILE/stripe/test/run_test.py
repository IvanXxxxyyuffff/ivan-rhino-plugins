# -*- coding: utf-8 -*-
# Auto test driver: load StripeOnSurface.rhp, run self test, capture viewport, exit.
import os, sys, traceback

LOG = r"C:\zcode_build\stripe\_run.log"

def log(s):
    try:
        with open(LOG, "a", encoding="utf-8") as f:
            f.write(str(s) + "\n")
    except Exception:
        pass

try:
    if os.path.exists(LOG):
        os.remove(LOG)
except Exception:
    pass

log("=== run_test.py start ===")

try:
    import System
    import Rhino

    log("rhino version: " + str(Rhino.RhinoApp.Version))

    rhp = r"C:\zcode_build\stripe\out\rhino8\StripeOnSurface.rhp"
    log("plugin exists: " + str(os.path.exists(rhp)))

    t = System.Type.GetType("Rhino.PlugIns.PlugIn, RhinoCommon")
    log("plugin type: " + str(t))

    methods = [m for m in t.GetMethods() if m.Name == "LoadPlugIn"]
    for m in methods:
        log("  overload: " + str(m) + " params=" + str([str(p.ParameterType) for p in m.GetParameters()]))

    loaded = False
    target = None
    for m in methods:
        ps = m.GetParameters()
        if len(ps) == 2 and ps[0].ParameterType == System.String:
            target = m
            break
    if target is None:
        log("ERROR: no LoadPlugIn(string, ref Guid) overload")
    else:
        try:
            args = System.Array[System.Object]([rhp, System.Guid.Empty])
            res = target.Invoke(None, args)
            log("LoadPlugIn -> " + str(res) + " id=" + str(args[1]))
            loaded = bool(res)
        except Exception as e:
            log("reflection invoke failed: " + str(e))
            try:
                g = System.Guid.Empty
                res = Rhino.PlugIns.PlugIn.LoadPlugIn(rhp, g)
                log("direct call -> " + str(res))
                loaded = bool(res)
            except Exception as e2:
                log("direct call failed: " + str(e2))

    log("loaded=" + str(loaded))

    # 查询命令是否注册
    try:
        names = [c.EnglishName for c in Rhino.Commands.Command.GetCommands()]
        log("StripeOnSurface registered: " + str("StripeOnSurface" in names))
        log("StripeSelfTest registered: " + str("StripeSelfTest" in names))
    except Exception as e:
        log("command query failed: " + str(e))

    if loaded:
        log("running self test ...")
        r = Rhino.RhinoApp.RunScript("-_StripeSelfTest Exit=No", False)
        log("self test runscript -> " + str(r))

        try:
            Rhino.RhinoApp.RunScript("-_Zoom _Extents", False)
            Rhino.RhinoApp.RunScript("-_ViewCaptureToFile " + r"C:\zcode_build\stripe\shot.png" + " Width=1280 Height=800", False)
            log("capture done: " + str(os.path.exists(r"C:\zcode_build\stripe\shot.png")))
        except Exception as e:
            log("capture failed: " + str(e))

    log("=== done ===")
except Exception:
    log("EXCEPTION:\n" + traceback.format_exc())

try:
    import Rhino
    Rhino.RhinoApp.Exit()
except Exception:
    pass

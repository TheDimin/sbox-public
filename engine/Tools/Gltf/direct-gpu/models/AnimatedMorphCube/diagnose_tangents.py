#!/usr/bin/env python3
"""Bounded source diagnosis for the retained failed tangent oracle; no rendering."""
import importlib.util
import inspect
import json
from pathlib import Path
import sys

path = Path(__file__).with_name("author_reference.py")
spec = importlib.util.spec_from_file_location("animated_morph_cube_failed_oracle", path)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
original = module.normalize


def diagnosed_normalize(vector):
    if sum(value*value for value in vector) == 0:
        caller = inspect.currentframe().f_back
        context = caller.f_locals
        outer = caller
        while outer is not None and outer.f_code.co_name != "author":
            outer = outer.f_back
        vertex = context.get("vertex")
        targets = context.get("deltas", [])
        print(json.dumps({"source": str(outer.f_locals.get("path")) if outer else None,
                          "requestedTime": context.get("requested_time"), "sampleTime": context.get("time"),
                          "vertex": vertex, "weights": context.get("sampled"),
                          "localNormal": context.get("n"), "localTangent": context.get("t"),
                          "worldNormal": context.get("world_normal"), "unprojectedWorldTangent": context.get("world_tangent"),
                          "normalProjection": context.get("projection"), "projectedTangent": vector,
                          "baseTangent": context.get("tangents")[vertex] if context.get("tangents") is not None else None,
                          "targetTangentDeltas": [target.get("TANGENT")[vertex] if "TANGENT" in target else None for target in targets],
                          "worldMatrix": context.get("matrix")}), flush=True)
    return original(vector)


module.normalize = diagnosed_normalize
module.main()

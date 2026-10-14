"""Quick check: mesh a car body and write an OBJ.  python -m carsmith.try_body kitsune out.obj [voxel]"""
import importlib
import sys
import time

from .body import make_body_sdf
from .mesher import surface_nets, write_obj


def main():
    car = sys.argv[1]
    out = sys.argv[2]
    h = float(sys.argv[3]) if len(sys.argv) > 3 else 0.012
    spec = importlib.import_module(f"carsmith.cars.{car}").SPEC
    fn = make_body_sdf(spec)
    bmin, bmax = spec.bounds()
    t = time.time()
    V, Q, N = surface_nets(fn, bmin, bmax, h)
    print(f"{car}: {len(V)} vertices, {len(Q)} quads in {time.time() - t:.1f}s")
    write_obj(out, V, Q, N)


if __name__ == "__main__":
    main()

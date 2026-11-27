"""Step 0.3 smoke check: can this machine's GPU do what the NV-Segment-CT engine needs? (not shipped)

Run from src/server, after the uv sync for this machine:
    uv run python tools/smoke.py [--out smoke-report.json]

Prints a short report and writes it as JSON to paste into the issue. Each check reports
pass / fail / n/a and never stops the others. Downloads the model files first if they are missing.
"""

import argparse
import ctypes
import json
import os
import platform
import shutil
import subprocess
import sys
import time
import traceback
from importlib.metadata import version
from pathlib import Path

import torch
import torch.nn.functional as F

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))

# The "everything" classes: 1-132 minus these (docs/demo-evals.md, NV-Segment-CT release facts).
EXCLUDED = {2, 16, 18, 20, 21, 23, 24, 25, 26, 27, 128, 129, 130, 131, 132}
EVERYTHING = [c for c in range(1, 133) if c not in EXCLUDED]
WINDOW = 128
GB = 1024**3


# ---------------------------------------------------------------- device helpers


def available() -> dict[str, bool]:
    return {
        "cuda": torch.cuda.is_available(),
        "xpu": hasattr(torch, "xpu") and torch.xpu.is_available(),
        "mps": torch.backends.mps.is_available(),
    }


def pick_device() -> torch.device:
    """CUDA -> XPU -> MPS -> CPU (plan's device order)."""
    for name, ok in available().items():
        if ok:
            return torch.device(name)
    return torch.device("cpu")


def sync(device: torch.device) -> None:
    if device.type in ("cuda", "xpu", "mps"):
        getattr(torch, device.type).synchronize()


def reset_peak(device: torch.device) -> None:
    if device.type in ("cuda", "xpu"):
        getattr(torch, device.type).reset_peak_memory_stats()
    elif device.type == "mps":
        torch.mps.empty_cache()


def peak_gb(device: torch.device) -> tuple[float | None, str]:
    if device.type in ("cuda", "xpu"):
        return round(getattr(torch, device.type).max_memory_allocated() / GB, 2), "max_memory_allocated"
    if device.type == "mps":  # MPS has no peak counter; this is what the driver holds after the run
        return round(torch.mps.driver_allocated_memory() / GB, 2), "driver_allocated_memory (after run, not a true peak)"
    return None, "not measured on CPU"


def system_ram_gb() -> float | None:
    if sys.platform == "win32":

        class Status(ctypes.Structure):
            _fields_ = [("dwLength", ctypes.c_ulong), ("dwMemoryLoad", ctypes.c_ulong)] + [
                (n, ctypes.c_ulonglong)
                for n in ("total", "avail", "totalPage", "availPage", "totalVirtual", "availVirtual", "availExt")
            ]

        status = Status(dwLength=ctypes.sizeof(Status))
        ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(status))
        return round(status.total / GB, 1)
    try:
        return round(os.sysconf("SC_PAGE_SIZE") * os.sysconf("SC_PHYS_PAGES") / GB, 1)
    except (ValueError, OSError, AttributeError):
        return None


def device_info(device: torch.device) -> dict:
    info: dict = {"device": device.type, "system_ram_gb": system_ram_gb()}
    if device.type in ("cuda", "xpu"):
        mod = getattr(torch, device.type)
        props = mod.get_device_properties(0)
        info["name"] = props.name
        free, total = mod.mem_get_info()
        info["memory_total_gb"] = round(total / GB, 2)
        info["memory_free_gb"] = round(free / GB, 2)
        if device.type == "cuda":
            info["compute_capability"] = f"{props.major}.{props.minor}"
            info["cuda_runtime"] = torch.version.cuda
            info["nvidia_driver"] = nvidia_driver()
    elif device.type == "mps":
        chip = subprocess.run(["sysctl", "-n", "machdep.cpu.brand_string"], capture_output=True, text=True, check=False)
        info["name"] = chip.stdout.strip() or "Apple GPU"
        info["memory_kind"] = "shared with system RAM"
        info["recommended_max_memory_gb"] = round(torch.mps.recommended_max_memory() / GB, 2)
    else:
        info["name"] = platform.processor() or platform.machine()
    return info


def nvidia_driver() -> str | None:
    if not shutil.which("nvidia-smi"):
        return None
    out = subprocess.run(
        ["nvidia-smi", "--query-gpu=driver_version", "--format=csv,noheader"], capture_output=True, text=True, check=False
    )
    return out.stdout.strip() or None


def folder_gb(path: Path) -> float:
    total = 0
    for root, _, files in os.walk(path):
        for name in files:
            try:
                total += (Path(root) / name).lstat().st_size
            except OSError:
                pass
    return round(total / GB, 2)


# ---------------------------------------------------------------- checks
# Each returns (status, details). An exception counts as "fail" with the error recorded.


def check_environment(device):
    return "pass", {
        "os": platform.platform(),
        "python": platform.python_version(),
        "torch": torch.__version__,
        "monai": version("monai"),
        "simpleitk": version("simpleitk"),
        **device_info(device),
    }


def check_device_order(device):
    found = available()
    expected = next((n for n, ok in found.items() if ok), "cpu")
    status = "pass" if device.type == expected else "fail"
    return status, {"available": found, "chosen": device.type}


def check_float64(device):
    if device.type == "cpu":
        return "n/a", {"note": "CPU is the fallback; float64 always works there"}
    try:
        x = torch.arange(8, dtype=torch.float64, device=device)
        y = (x * x).sum().item()
        return "pass", {"result": y}
    except Exception as error:  # MPS is expected to land here
        note = "expected on MPS: float64 geometry stays on the CPU (plan)" if device.type == "mps" else None
        return "fail", {"error": f"{type(error).__name__}: {error}", "note": note}


def check_grid_sample(device):
    gen = torch.Generator().manual_seed(0)
    image = torch.rand(1, 2, 40, 36, 32, generator=gen)
    grid = torch.rand(1, 24, 20, 16, 3, generator=gen) * 2.2 - 1.1  # some points outside, to exercise border padding
    def sample(img, grd):
        return F.grid_sample(img, grd, mode="bilinear", padding_mode="border", align_corners=False)

    expected = sample(image, grid)
    got = sample(image.to(device), grid.to(device))
    diff = (got.cpu() - expected).abs().max().item()
    return ("pass" if diff < 1e-4 else "fail"), {"max_abs_diff_vs_cpu": diff, "tolerance": 1e-4}


def check_reduced_precision(device):
    gen = torch.Generator().manual_seed(0)
    x = torch.randn(1, 8, 32, 32, 32, generator=gen).to(device)
    w = (torch.randn(16, 8, 3, 3, 3, generator=gen) * 0.1).to(device)
    reference = F.conv3d(x, w, padding=1)
    results = {}
    for name, dtype in (("fp16", torch.float16), ("bf16", torch.bfloat16)):
        try:
            with torch.autocast(device.type, dtype=dtype):
                out = F.conv3d(x, w, padding=1)
            sync(device)
            results[name] = {
                "runs": True,
                "output_dtype": str(out.dtype),
                "max_abs_diff_vs_fp32": (out.float() - reference).abs().max().item(),
                "reference_max_abs": reference.abs().max().item(),
            }
        except Exception as error:
            results[name] = {"runs": False, "error": f"{type(error).__name__}: {error}"}
    status = "pass" if any(r["runs"] for r in results.values()) else "fail"
    return status, results


def check_model_load(device, state):
    from monai.networks.nets.vista3d import vista3d132
    from safetensors.torch import load_file

    from segmentation.weights import WEIGHTS, ensure_weights  # pyright: ignore[reportMissingImports] (src/ added to sys.path above)

    files = ensure_weights()
    weights = {k.removeprefix("network."): v for k, v in load_file(files[WEIGHTS]).items()}
    model = vista3d132(encoder_embed_dim=48, in_channels=1)
    model.load_state_dict(weights, strict=True)  # raises on any missing or unexpected key
    state["model"] = model.to(device).eval()
    state["weights_dir"] = files[WEIGHTS].parents[1]
    return "pass", {"keys": len(weights), "file": str(files[WEIGHTS])}


def check_one_window(device, state, precisions):
    if "model" not in state:
        return "n/a", {"note": "model did not load"}
    model = state["model"]
    image = torch.rand(1, 1, WINDOW, WINDOW, WINDOW, generator=torch.Generator().manual_seed(0)).to(device)
    classes = torch.tensor(EVERYTHING, device=device).unsqueeze(1)  # [117, 1]
    coords = [[slice(0, 1), slice(None)] + [slice(0, WINDOW)] * 3]  # what with_coord=True passes for one window

    def run(dtype):
        with torch.inference_mode(), torch.autocast(device.type, dtype=dtype, enabled=dtype is not None):
            out = model(image, patch_coords=coords, class_vector=classes, transpose=True)
        sync(device)
        return out

    results = {}
    for name, dtype in [("fp32", None)] + [(p, getattr(torch, {"fp16": "float16", "bf16": "bfloat16"}[p])) for p in precisions]:
        try:
            run(dtype)  # warm-up, not timed
            reset_peak(device)
            start = time.perf_counter()
            out = run(dtype)
            seconds = time.perf_counter() - start
            peak, how = peak_gb(device)
            results[name] = {
                "seconds": round(seconds, 3),
                "peak_memory_gb": peak,
                "peak_memory_source": how,
                "output_shape": list(out.shape),
                "finite": bool(torch.isfinite(out).all().item()),
            }
            del out
        except Exception as error:
            results[name] = {"error": f"{type(error).__name__}: {error}"}
    ok = "seconds" in results.get("fp32", {}) and results["fp32"]["output_shape"][1] == len(EVERYTHING)
    return ("pass" if ok else "fail"), results


def check_disk(state):
    details = {"venv_gb": folder_gb(Path(sys.prefix)), "venv": sys.prefix}
    if "weights_dir" in state:
        details["weights_gb"] = folder_gb(state["weights_dir"])
    return "pass", details


# ---------------------------------------------------------------- runner


def main() -> None:
    parser = argparse.ArgumentParser(description=(__doc__ or "").splitlines()[0])
    parser.add_argument("--out", type=Path, default=Path("smoke-report.json"))
    out_path = parser.parse_args().out

    device = pick_device()
    state: dict = {}
    report: dict = {"date": time.strftime("%Y-%m-%d %H:%M"), "checks": {}}

    def record(name, fn, *args) -> tuple[str, dict]:
        print(f"... {name}", flush=True)
        try:
            status, details = fn(*args)
        except Exception as error:
            status, details = "fail", {
                "error": f"{type(error).__name__}: {error}",
                "traceback": traceback.format_exc(limit=3),
            }
        report["checks"][name] = {"status": status, **details}
        return status, details

    record("1 environment", check_environment, device)
    record("2 device order", check_device_order, device)
    record("3 float64 on device", check_float64, device)
    record("4 grid_sample 3D float32", check_grid_sample, device)
    _, precision = record("5 reduced precision", check_reduced_precision, device)
    record("6 model load", check_model_load, device, state)
    ran = [p for p in ("fp16", "bf16") if precision.get(p, {}).get("runs")]
    record("7 one window (117 classes)", check_one_window, device, state, ran)
    record("8 disk", check_disk, state)

    print()
    for name, result in report["checks"].items():
        print(f"{result['status']:>5}  {name}")
    env = report["checks"]["1 environment"]
    print(f"\ndevice: {env.get('device')} ({env.get('name')}), torch {env.get('torch')}, monai {env.get('monai')}")
    for name, r in report["checks"]["7 one window (117 classes)"].items():
        if isinstance(r, dict) and "seconds" in r:
            print(f"window {name}: {r['seconds']} s, peak {r['peak_memory_gb']} GB")
    out_path.write_text(json.dumps(report, indent=2, default=str))
    print(f"\nJSON report: {out_path.resolve()}")


if __name__ == "__main__":
    main()

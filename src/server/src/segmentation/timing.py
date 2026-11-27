import time
from collections.abc import Generator
from contextlib import contextmanager


class StageTimer:
    """Seconds per stage of one job; the engine and the pipeline share it: with timer("inference"): ..."""

    def __init__(self) -> None:
        self.seconds: dict[str, float] = {}

    @contextmanager
    def __call__(self, stage: str) -> Generator[None]:
        start = time.perf_counter()
        try:
            yield
        finally:
            self.seconds[stage] = self.seconds.get(stage, 0.0) + time.perf_counter() - start

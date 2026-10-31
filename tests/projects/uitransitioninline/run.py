#!/usr/bin/env python3
import importlib.util
from pathlib import Path

spec = importlib.util.spec_from_file_location('transition_driver', Path(__file__).resolve().parent.parent / 'uitransitions/run.py')
driver = importlib.util.module_from_spec(spec)
spec.loader.exec_module(driver)
if __name__ == '__main__':
    driver.run(Path(__file__).resolve().parent, 'TransitionInlineRunner.Record', 'UITRANSITIONINLINE_OUT')

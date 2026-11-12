"""Scheduling barriers shared by landing drivers and probe suites."""


def parallel_groups(items, exclusive):
    """Yield concurrent groups in input order, with each exclusive item alone."""
    pending = []
    for item in items:
        if exclusive(item):
            if pending:
                yield pending
                pending = []
            yield [item]
        else:
            pending.append(item)
    if pending:
        yield pending

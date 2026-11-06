//! An unstable introsort: median-of-three quicksort above 16 elements, heapsort past the depth
//! limit, then insertion sort. The order it leaves equal elements in is part of the result.

fn insertion_sort<T: Copy>(v: &mut [T], less: &impl Fn(&T, &T) -> bool) {
    for i in 1..v.len() {
        if less(&v[i], &v[0]) {
            let value = v[i];
            v.copy_within(0..i, 1);
            v[0] = value;
        } else {
            unguarded_linear_insert(v, i, less);
        }
    }
}

fn unguarded_linear_insert<T: Copy>(v: &mut [T], mut last: usize, less: &impl Fn(&T, &T) -> bool) {
    let value = v[last];
    let mut next = last - 1;
    while less(&value, &v[next]) {
        v[last] = v[next];
        last = next;
        next -= 1;
    }
    v[last] = value;
}

fn move_median_to_first<T: Copy>(
    v: &mut [T],
    result: usize,
    a: usize,
    b: usize,
    c: usize,
    less: &impl Fn(&T, &T) -> bool,
) {
    let pick = if less(&v[a], &v[b]) {
        if less(&v[b], &v[c]) {
            b
        } else if less(&v[a], &v[c]) {
            c
        } else {
            a
        }
    } else if less(&v[a], &v[c]) {
        a
    } else if less(&v[b], &v[c]) {
        c
    } else {
        b
    };
    v.swap(result, pick);
}

fn unguarded_partition<T: Copy>(
    v: &mut [T],
    mut first: usize,
    mut last: usize,
    pivot: usize,
    less: &impl Fn(&T, &T) -> bool,
) -> usize {
    loop {
        while less(&v[first], &v[pivot]) {
            first += 1;
        }
        last -= 1;
        while less(&v[pivot], &v[last]) {
            last -= 1;
        }
        if first >= last {
            return first;
        }
        v.swap(first, last);
        first += 1;
    }
}

fn adjust_heap<T: Copy>(
    v: &mut [T],
    mut hole: usize,
    len: usize,
    value: T,
    less: &impl Fn(&T, &T) -> bool,
) {
    let top = hole;
    let mut child = hole;
    while child < (len - 1) / 2 {
        child = 2 * (child + 1);
        if less(&v[child], &v[child - 1]) {
            child -= 1;
        }
        v[hole] = v[child];
        hole = child;
    }
    if len & 1 == 0 && child == (len - 2) / 2 {
        child = 2 * (child + 1);
        v[hole] = v[child - 1];
        hole = child - 1;
    }
    let mut parent = hole.wrapping_sub(1) / 2;
    while hole > top && less(&v[parent], &value) {
        v[hole] = v[parent];
        hole = parent;
        parent = hole.wrapping_sub(1) / 2;
    }
    v[hole] = value;
}

fn heap_sort<T: Copy>(v: &mut [T], less: &impl Fn(&T, &T) -> bool) {
    let len = v.len();
    if len >= 2 {
        let mut parent = (len - 2) / 2;
        loop {
            let value = v[parent];
            adjust_heap(v, parent, len, value, less);
            if parent == 0 {
                break;
            }
            parent -= 1;
        }
    }
    let mut last = len;
    while last > 1 {
        last -= 1;
        let value = v[last];
        v[last] = v[0];
        adjust_heap(&mut v[..last], 0, last, value, less);
    }
}

fn introsort_loop<T: Copy>(v: &mut [T], mut depth: u32, less: &impl Fn(&T, &T) -> bool) {
    let mut last = v.len();
    while last > 16 {
        if depth == 0 {
            heap_sort(&mut v[..last], less);
            return;
        }
        depth -= 1;
        let mid = last / 2;
        move_median_to_first(v, 0, 1, mid, last - 1, less);
        let cut = unguarded_partition(v, 1, last, 0, less);
        introsort_loop(&mut v[cut..last], depth, less);
        last = cut;
    }
}

pub fn introsort<T: Copy>(v: &mut [T], less: impl Fn(&T, &T) -> bool) {
    let n = v.len();
    if n == 0 {
        return;
    }
    introsort_loop(v, 2 * (usize::BITS - 1 - n.leading_zeros()), &less);
    if n > 16 {
        insertion_sort(&mut v[..16], &less);
        for i in 16..n {
            unguarded_linear_insert(v, i, &less);
        }
    } else {
        insertion_sort(v, &less);
    }
}

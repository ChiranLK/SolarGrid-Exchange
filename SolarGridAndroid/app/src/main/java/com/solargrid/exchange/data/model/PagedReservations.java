package com.solargrid.exchange.data.model;

import java.util.Collections;
import java.util.ArrayList;
import java.util.List;

public final class PagedReservations {
    private final List<Reservation> items;
    private final long totalCount;
    private final int page;
    private final int totalPages;

    public PagedReservations(List<Reservation> items, long totalCount, int page, int totalPages) {
        this.items = Collections.unmodifiableList(new ArrayList<>(items));
        this.totalCount = totalCount;
        this.page = page;
        this.totalPages = totalPages;
    }

    public List<Reservation> getItems() { return items; }
    public long getTotalCount() { return totalCount; }
    public int getPage() { return page; }
    public int getTotalPages() { return totalPages; }
}

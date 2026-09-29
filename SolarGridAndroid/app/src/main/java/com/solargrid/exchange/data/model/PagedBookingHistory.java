package com.solargrid.exchange.data.model;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

public final class PagedBookingHistory {
    private final List<DashboardReservation> items;
    private final long totalCount;
    private final int page;
    private final int pageSize;
    private final int totalPages;

    public PagedBookingHistory(
            List<DashboardReservation> items,
            long totalCount,
            int page,
            int pageSize,
            int totalPages) {
        this.items = Collections.unmodifiableList(new ArrayList<>(items));
        this.totalCount = totalCount;
        this.page = page;
        this.pageSize = pageSize;
        this.totalPages = totalPages;
    }

    public List<DashboardReservation> getItems() { return items; }
    public long getTotalCount() { return totalCount; }
    public int getPage() { return page; }
    public int getPageSize() { return pageSize; }
    public int getTotalPages() { return totalPages; }
}

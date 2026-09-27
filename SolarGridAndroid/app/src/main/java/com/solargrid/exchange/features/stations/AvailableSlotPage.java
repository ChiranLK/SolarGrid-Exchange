package com.solargrid.exchange.features.stations;

import com.solargrid.exchange.data.model.Slot;

import java.util.List;

public final class AvailableSlotPage {
    private final List<Slot> items;
    private final int page;
    private final int totalPages;

    public AvailableSlotPage(List<Slot> items, int page, int totalPages) {
        this.items = items;
        this.page = page;
        this.totalPages = totalPages;
    }

    public List<Slot> getItems() { return items; }
    public int getPage() { return page; }
    public int getTotalPages() { return totalPages; }
}

package com.solargrid.exchange.features.dashboard;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertTrue;

import org.junit.Test;

public final class BookingHistoryQueryTest {
    @Test
    public void trimsAndEncodesEverySupportedServerFilter() {
        BookingHistoryQuery query = new BookingHistoryQuery(
                "  RES 10  ",
                " Completed ",
                " 0123456789abcdef01234567 ",
                "2026-09-01T00:00:00.000Z",
                "2026-09-30T23:59:59.999Z",
                2);

        assertEquals(
                "dashboard/history?page=2&pageSize=20&search=RES+10&status=Completed" +
                        "&stationId=0123456789abcdef01234567" +
                        "&fromUtc=2026-09-01T00%3A00%3A00.000Z" +
                        "&toUtc=2026-09-30T23%3A59%3A59.999Z",
                query.toRelativePath());
    }

    @Test
    public void omitsBlankOptionalFilters() {
        assertEquals(
                "dashboard/history?page=1&pageSize=20",
                new BookingHistoryQuery(" ", "", null, "", "", 1).toRelativePath());
    }

    @Test
    public void rejectsInvalidDateRangeAndPageBeforeNetworkCall() {
        BookingHistoryQuery dates = new BookingHistoryQuery(
                "", "", "", "2026-10-02T00:00:00.000Z", "2026-10-01T23:59:59.999Z", 1);
        BookingHistoryQuery page = new BookingHistoryQuery("", "", "", "", "", 0);

        assertTrue(dates.validationMessage().contains("from date"));
        assertTrue(page.validationMessage().contains("Page"));
    }
}

package com.solargrid.exchange.data.local;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertNotNull;

import android.content.Context;
import android.database.sqlite.SQLiteDatabase;

import androidx.test.core.app.ApplicationProvider;
import androidx.test.ext.junit.runners.AndroidJUnit4;

import com.solargrid.exchange.data.model.Slot;
import com.solargrid.exchange.data.model.Station;

import org.junit.After;
import org.junit.Before;
import org.junit.Test;
import org.junit.runner.RunWith;

import java.util.Collections;

/** Real SQLite coverage for durable station/slot reference data and its relationship. */
@RunWith(AndroidJUnit4.class)
public final class ReferenceDataStoreInstrumentedTest {
    private SessionDatabaseHelper helper;
    private ReferenceDataStore store;

    @Before
    public void setUp() {
        // Start each test with empty reference tables while leaving session behaviour independent.
        Context context = ApplicationProvider.getApplicationContext();
        helper = new SessionDatabaseHelper(context);
        SQLiteDatabase database = helper.getWritableDatabase();
        database.delete(SessionDatabaseHelper.TABLE_SLOTS, null, null);
        database.delete(SessionDatabaseHelper.TABLE_STATIONS, null, null);
        store = new ReferenceDataStore(helper);
    }

    @After
    public void tearDown() {
        // Remove synthetic reference rows and close the shared helper.
        SQLiteDatabase database = helper.getWritableDatabase();
        database.delete(SessionDatabaseHelper.TABLE_SLOTS, null, null);
        database.delete(SessionDatabaseHelper.TABLE_STATIONS, null, null);
        helper.close();
    }

    @Test
    public void stationAndSlotsRoundTripThroughReferenceTables() {
        // Proves the cache persists both projections and reconstructs their station reference.
        Station station = new Station(
                "station-1", "Solar Hub", "Reference station", "Colombo",
                6.9, 79.8, 50, 120, true);
        Slot slot = new Slot(
                "slot-1", station.getId(), "2026-10-06T03:00:00Z",
                "2026-10-06T04:00:00Z", 25, "Available");

        store.upsertStation(station);
        store.replaceSlots(station.getId(), Collections.singletonList(slot));

        assertNotNull(store.readStation(station.getId()));
        assertEquals(1, store.readActiveStations().size());
        assertEquals(1, store.readSlots(station.getId()).size());
        assertEquals(station.getId(), store.readSlots(station.getId()).get(0).getStationId());
    }

    @Test
    public void replacingStationSlotsRemovesStaleReferenceRows() {
        // Proves a fresh server response replaces, rather than merges with, stale slot availability.
        Station station = new Station(
                "station-2", "Community Hub", "", "Kandy",
                7.2, 80.6, 30, 80, true);
        store.upsertStation(station);
        store.replaceSlots(station.getId(), Collections.singletonList(new Slot(
                "old-slot", station.getId(), "2026-10-06T03:00:00Z",
                "2026-10-06T04:00:00Z", 10, "Available")));

        store.replaceSlots(station.getId(), Collections.emptyList());

        assertEquals(0, store.readSlots(station.getId()).size());
    }
}

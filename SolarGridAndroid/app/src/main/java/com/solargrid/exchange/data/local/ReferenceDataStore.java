package com.solargrid.exchange.data.local;

import android.content.ContentValues;
import android.database.Cursor;
import android.database.sqlite.SQLiteDatabase;

import androidx.annotation.Nullable;

import com.solargrid.exchange.data.model.Slot;
import com.solargrid.exchange.data.model.Station;

import java.util.ArrayList;
import java.util.List;

/** Durable SQLite cache for station and slot reference data returned by the central API. */
public final class ReferenceDataStore {
    private final SessionDatabaseHelper databaseHelper;

    public ReferenceDataStore(SessionDatabaseHelper databaseHelper) {
        // Share the application's versioned SQLite helper with the session store.
        this.databaseHelper = databaseHelper;
    }

    public synchronized void upsertStations(List<Station> stations) {
        // Save the latest server projections atomically without deleting unrelated cached stations.
        SQLiteDatabase database = databaseHelper.getWritableDatabase();
        database.beginTransaction();
        try {
            long now = System.currentTimeMillis();
            for (Station station : stations) {
                ContentValues values = stationValues(station, now);
                int updated = database.update(
                        SessionDatabaseHelper.TABLE_STATIONS,
                        values,
                        SessionDatabaseHelper.COLUMN_STATION_ID + " = ?",
                        new String[]{station.getId()});
                if (updated == 0) {
                    database.insertOrThrow(
                            SessionDatabaseHelper.TABLE_STATIONS,
                            null,
                            values);
                }
            }
            database.setTransactionSuccessful();
        } finally {
            database.endTransaction();
        }
    }

    public synchronized void upsertStation(Station station) {
        // Reuse the batch path so a detail response follows the same conflict policy.
        List<Station> stations = new ArrayList<>();
        stations.add(station);
        upsertStations(stations);
    }

    public synchronized List<Station> readActiveStations() {
        // Return cached active stations in a deterministic name/id order for offline UI use.
        SQLiteDatabase database = databaseHelper.getReadableDatabase();
        List<Station> stations = new ArrayList<>();
        try (Cursor cursor = database.query(
                SessionDatabaseHelper.TABLE_STATIONS,
                null,
                SessionDatabaseHelper.COLUMN_ACTIVE + " = ?",
                new String[]{"1"},
                null,
                null,
                SessionDatabaseHelper.COLUMN_NAME + " COLLATE NOCASE, " +
                        SessionDatabaseHelper.COLUMN_STATION_ID)) {
            while (cursor.moveToNext()) stations.add(readStation(cursor));
        }
        return stations;
    }

    @Nullable
    public synchronized Station readStation(String stationId) {
        // Load one cached station by its server identifier.
        SQLiteDatabase database = databaseHelper.getReadableDatabase();
        try (Cursor cursor = database.query(
                SessionDatabaseHelper.TABLE_STATIONS,
                null,
                SessionDatabaseHelper.COLUMN_STATION_ID + " = ?",
                new String[]{stationId},
                null,
                null,
                null,
                "1")) {
            return cursor.moveToFirst() ? readStation(cursor) : null;
        }
    }

    public synchronized void replaceSlots(String stationId, List<Slot> slots) {
        // Replace one station's reference slots atomically after a successful server read.
        SQLiteDatabase database = databaseHelper.getWritableDatabase();
        database.beginTransaction();
        try {
            database.delete(
                    SessionDatabaseHelper.TABLE_SLOTS,
                    SessionDatabaseHelper.COLUMN_STATION_ID + " = ?",
                    new String[]{stationId});
            long now = System.currentTimeMillis();
            for (Slot slot : slots) {
                database.insertWithOnConflict(
                        SessionDatabaseHelper.TABLE_SLOTS,
                        null,
                        slotValues(slot, now),
                        SQLiteDatabase.CONFLICT_REPLACE);
            }
            database.setTransactionSuccessful();
        } finally {
            database.endTransaction();
        }
    }

    public synchronized List<Slot> readSlots(String stationId) {
        // Return cached slots in chronological order for offline station/reference screens.
        SQLiteDatabase database = databaseHelper.getReadableDatabase();
        List<Slot> slots = new ArrayList<>();
        try (Cursor cursor = database.query(
                SessionDatabaseHelper.TABLE_SLOTS,
                null,
                SessionDatabaseHelper.COLUMN_STATION_ID + " = ?",
                new String[]{stationId},
                null,
                null,
                SessionDatabaseHelper.COLUMN_START_TIME + ", " + SessionDatabaseHelper.COLUMN_SLOT_ID)) {
            while (cursor.moveToNext()) {
                slots.add(new Slot(
                        text(cursor, SessionDatabaseHelper.COLUMN_SLOT_ID),
                        text(cursor, SessionDatabaseHelper.COLUMN_STATION_ID),
                        text(cursor, SessionDatabaseHelper.COLUMN_START_TIME),
                        text(cursor, SessionDatabaseHelper.COLUMN_END_TIME),
                        cursor.getDouble(cursor.getColumnIndexOrThrow(
                                SessionDatabaseHelper.COLUMN_AVAILABLE_CAPACITY)),
                        text(cursor, SessionDatabaseHelper.COLUMN_AVAILABILITY_STATUS)));
            }
        }
        return slots;
    }

    private static ContentValues stationValues(Station station, long updatedAt) {
        // Map the immutable station model to SQLite columns without storing credentials.
        ContentValues values = new ContentValues();
        values.put(SessionDatabaseHelper.COLUMN_STATION_ID, station.getId());
        values.put(SessionDatabaseHelper.COLUMN_NAME, station.getName());
        values.put(SessionDatabaseHelper.COLUMN_DESCRIPTION, station.getDescription());
        values.put(SessionDatabaseHelper.COLUMN_ADDRESS, station.getAddress());
        values.put(SessionDatabaseHelper.COLUMN_LATITUDE, station.getLatitude());
        values.put(SessionDatabaseHelper.COLUMN_LONGITUDE, station.getLongitude());
        values.put(SessionDatabaseHelper.COLUMN_GENERATION_CAPACITY, station.getGenerationCapacityKw());
        values.put(SessionDatabaseHelper.COLUMN_STORAGE_CAPACITY, station.getStorageCapacityKwh());
        values.put(SessionDatabaseHelper.COLUMN_ACTIVE, station.isActive() ? 1 : 0);
        values.put(SessionDatabaseHelper.COLUMN_UPDATED_AT, updatedAt);
        return values;
    }

    private static ContentValues slotValues(Slot slot, long updatedAt) {
        // Map one server slot projection to the station-scoped reference table.
        ContentValues values = new ContentValues();
        values.put(SessionDatabaseHelper.COLUMN_SLOT_ID, slot.getId());
        values.put(SessionDatabaseHelper.COLUMN_STATION_ID, slot.getStationId());
        values.put(SessionDatabaseHelper.COLUMN_START_TIME, slot.getStartTimeUtc());
        values.put(SessionDatabaseHelper.COLUMN_END_TIME, slot.getEndTimeUtc());
        values.put(SessionDatabaseHelper.COLUMN_AVAILABLE_CAPACITY, slot.getAvailableCapacityKwh());
        values.put(SessionDatabaseHelper.COLUMN_AVAILABILITY_STATUS, slot.getAvailabilityStatus());
        values.put(SessionDatabaseHelper.COLUMN_UPDATED_AT, updatedAt);
        return values;
    }

    private static Station readStation(Cursor cursor) {
        // Reconstruct a station model from a cache row.
        return new Station(
                text(cursor, SessionDatabaseHelper.COLUMN_STATION_ID),
                text(cursor, SessionDatabaseHelper.COLUMN_NAME),
                text(cursor, SessionDatabaseHelper.COLUMN_DESCRIPTION),
                text(cursor, SessionDatabaseHelper.COLUMN_ADDRESS),
                cursor.getDouble(cursor.getColumnIndexOrThrow(SessionDatabaseHelper.COLUMN_LATITUDE)),
                cursor.getDouble(cursor.getColumnIndexOrThrow(SessionDatabaseHelper.COLUMN_LONGITUDE)),
                cursor.getDouble(cursor.getColumnIndexOrThrow(SessionDatabaseHelper.COLUMN_GENERATION_CAPACITY)),
                cursor.getDouble(cursor.getColumnIndexOrThrow(SessionDatabaseHelper.COLUMN_STORAGE_CAPACITY)),
                cursor.getInt(cursor.getColumnIndexOrThrow(SessionDatabaseHelper.COLUMN_ACTIVE)) == 1);
    }

    private static String text(Cursor cursor, String column) {
        // Read one required text column using a checked column lookup.
        return cursor.getString(cursor.getColumnIndexOrThrow(column));
    }
}

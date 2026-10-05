package com.solargrid.exchange.data.local;

import android.content.Context;
import android.database.sqlite.SQLiteDatabase;
import android.database.sqlite.SQLiteOpenHelper;

public final class SessionDatabaseHelper extends SQLiteOpenHelper {
    public static final String DATABASE_NAME = "solargrid_local.db";
    public static final int DATABASE_VERSION = 2;

    static final String TABLE_SESSION = "authenticated_session";
    static final String COLUMN_ID = "id";
    static final String COLUMN_TOKEN = "access_token";
    static final String COLUMN_NIC = "nic";
    static final String COLUMN_FULL_NAME = "full_name";
    static final String COLUMN_EMAIL = "email";
    static final String COLUMN_ROLE = "role";
    static final String COLUMN_STATUS = "status";
    static final String COLUMN_UPDATED_AT = "updated_at_epoch_ms";

    static final String TABLE_STATIONS = "reference_stations";
    static final String TABLE_SLOTS = "reference_slots";
    static final String COLUMN_STATION_ID = "station_id";
    static final String COLUMN_NAME = "name";
    static final String COLUMN_DESCRIPTION = "description";
    static final String COLUMN_ADDRESS = "address";
    static final String COLUMN_LATITUDE = "latitude";
    static final String COLUMN_LONGITUDE = "longitude";
    static final String COLUMN_GENERATION_CAPACITY = "generation_capacity_kw";
    static final String COLUMN_STORAGE_CAPACITY = "storage_capacity_kwh";
    static final String COLUMN_ACTIVE = "is_active";
    static final String COLUMN_SLOT_ID = "slot_id";
    static final String COLUMN_START_TIME = "start_time_utc";
    static final String COLUMN_END_TIME = "end_time_utc";
    static final String COLUMN_AVAILABLE_CAPACITY = "available_capacity_kwh";
    static final String COLUMN_AVAILABILITY_STATUS = "availability_status";

    private static final String CREATE_SESSION_TABLE =
            "CREATE TABLE " + TABLE_SESSION + " (" +
                    COLUMN_ID + " INTEGER PRIMARY KEY CHECK (" + COLUMN_ID + " = 1), " +
                    COLUMN_TOKEN + " TEXT NOT NULL, " +
                    COLUMN_NIC + " TEXT NOT NULL, " +
                    COLUMN_FULL_NAME + " TEXT NOT NULL, " +
                    COLUMN_EMAIL + " TEXT NOT NULL DEFAULT '', " +
                    COLUMN_ROLE + " TEXT NOT NULL, " +
                    COLUMN_STATUS + " TEXT NOT NULL, " +
                    COLUMN_UPDATED_AT + " INTEGER NOT NULL" +
                    ")";

    private static final String CREATE_STATIONS_TABLE =
            "CREATE TABLE " + TABLE_STATIONS + " (" +
                    COLUMN_STATION_ID + " TEXT PRIMARY KEY, " +
                    COLUMN_NAME + " TEXT NOT NULL, " +
                    COLUMN_DESCRIPTION + " TEXT NOT NULL DEFAULT '', " +
                    COLUMN_ADDRESS + " TEXT NOT NULL DEFAULT '', " +
                    COLUMN_LATITUDE + " REAL NOT NULL, " +
                    COLUMN_LONGITUDE + " REAL NOT NULL, " +
                    COLUMN_GENERATION_CAPACITY + " REAL NOT NULL, " +
                    COLUMN_STORAGE_CAPACITY + " REAL NOT NULL, " +
                    COLUMN_ACTIVE + " INTEGER NOT NULL, " +
                    COLUMN_UPDATED_AT + " INTEGER NOT NULL" +
                    ")";

    private static final String CREATE_SLOTS_TABLE =
            "CREATE TABLE " + TABLE_SLOTS + " (" +
                    COLUMN_SLOT_ID + " TEXT PRIMARY KEY, " +
                    COLUMN_STATION_ID + " TEXT NOT NULL, " +
                    COLUMN_START_TIME + " TEXT NOT NULL, " +
                    COLUMN_END_TIME + " TEXT NOT NULL, " +
                    COLUMN_AVAILABLE_CAPACITY + " REAL NOT NULL, " +
                    COLUMN_AVAILABILITY_STATUS + " TEXT NOT NULL, " +
                    COLUMN_UPDATED_AT + " INTEGER NOT NULL, " +
                    "FOREIGN KEY (" + COLUMN_STATION_ID + ") REFERENCES " +
                    TABLE_STATIONS + "(" + COLUMN_STATION_ID + ") ON DELETE CASCADE" +
                    ")";

    public SessionDatabaseHelper(Context context) {
        super(context, DATABASE_NAME, null, DATABASE_VERSION);
    }

    @Override
    public void onConfigure(SQLiteDatabase database) {
        super.onConfigure(database);
        database.setForeignKeyConstraintsEnabled(true);
    }

    @Override
    public void onCreate(SQLiteDatabase database) {
        // Create durable authentication and reference-data tables in one schema version.
        database.execSQL(CREATE_SESSION_TABLE);
        database.execSQL(CREATE_STATIONS_TABLE);
        database.execSQL(CREATE_SLOTS_TABLE);
    }

    @Override
    public void onUpgrade(SQLiteDatabase database, int oldVersion, int newVersion) {
        // Add explicit, forward-only migrations here. Never drop the session table as a fallback.
        if (oldVersion < 1) {
            database.execSQL(CREATE_SESSION_TABLE);
        }
        if (oldVersion < 2) {
            // Preserve the existing authenticated session while adding the offline reference cache.
            database.execSQL(CREATE_STATIONS_TABLE);
            database.execSQL(CREATE_SLOTS_TABLE);
        }
    }
}

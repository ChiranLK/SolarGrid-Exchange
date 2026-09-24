package com.solargrid.exchange.data.local;

import android.content.Context;
import android.database.sqlite.SQLiteDatabase;
import android.database.sqlite.SQLiteOpenHelper;

public final class SessionDatabaseHelper extends SQLiteOpenHelper {
    public static final String DATABASE_NAME = "solargrid_local.db";
    public static final int DATABASE_VERSION = 1;

    static final String TABLE_SESSION = "authenticated_session";
    static final String COLUMN_ID = "id";
    static final String COLUMN_TOKEN = "access_token";
    static final String COLUMN_NIC = "nic";
    static final String COLUMN_FULL_NAME = "full_name";
    static final String COLUMN_EMAIL = "email";
    static final String COLUMN_ROLE = "role";
    static final String COLUMN_STATUS = "status";
    static final String COLUMN_UPDATED_AT = "updated_at_epoch_ms";

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
        database.execSQL(CREATE_SESSION_TABLE);
    }

    @Override
    public void onUpgrade(SQLiteDatabase database, int oldVersion, int newVersion) {
        // Add explicit, forward-only migrations here. Never drop the session table as a fallback.
        if (oldVersion < 1) {
            database.execSQL(CREATE_SESSION_TABLE);
        }
    }
}

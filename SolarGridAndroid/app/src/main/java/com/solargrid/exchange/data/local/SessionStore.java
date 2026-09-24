package com.solargrid.exchange.data.local;

import android.content.ContentValues;
import android.database.Cursor;
import android.database.sqlite.SQLiteDatabase;

import androidx.annotation.Nullable;

import com.solargrid.exchange.data.model.SessionUser;

public final class SessionStore {
    private final SessionDatabaseHelper databaseHelper;

    public SessionStore(SessionDatabaseHelper databaseHelper) {
        this.databaseHelper = databaseHelper;
    }

    public synchronized void save(SessionUser session) {
        SQLiteDatabase database = databaseHelper.getWritableDatabase();
        ContentValues values = new ContentValues();
        values.put(SessionDatabaseHelper.COLUMN_ID, 1);
        values.put(SessionDatabaseHelper.COLUMN_TOKEN, session.getToken());
        values.put(SessionDatabaseHelper.COLUMN_NIC, session.getNic());
        values.put(SessionDatabaseHelper.COLUMN_FULL_NAME, session.getFullName());
        values.put(SessionDatabaseHelper.COLUMN_EMAIL, session.getEmail());
        values.put(SessionDatabaseHelper.COLUMN_ROLE, session.getRole());
        values.put(SessionDatabaseHelper.COLUMN_STATUS, session.getStatus());
        values.put(SessionDatabaseHelper.COLUMN_UPDATED_AT, System.currentTimeMillis());

        database.beginTransaction();
        try {
            database.insertWithOnConflict(
                    SessionDatabaseHelper.TABLE_SESSION,
                    null,
                    values,
                    SQLiteDatabase.CONFLICT_REPLACE);
            database.setTransactionSuccessful();
        } finally {
            database.endTransaction();
        }
    }

    @Nullable
    public synchronized SessionUser read() {
        SQLiteDatabase database = databaseHelper.getReadableDatabase();
        String selection = SessionDatabaseHelper.COLUMN_ID + " = ?";
        try (Cursor cursor = database.query(
                SessionDatabaseHelper.TABLE_SESSION,
                null,
                selection,
                new String[]{"1"},
                null,
                null,
                null,
                "1")) {
            if (!cursor.moveToFirst()) {
                return null;
            }
            return new SessionUser(
                    getString(cursor, SessionDatabaseHelper.COLUMN_TOKEN),
                    getString(cursor, SessionDatabaseHelper.COLUMN_NIC),
                    getString(cursor, SessionDatabaseHelper.COLUMN_FULL_NAME),
                    getString(cursor, SessionDatabaseHelper.COLUMN_EMAIL),
                    getString(cursor, SessionDatabaseHelper.COLUMN_ROLE),
                    getString(cursor, SessionDatabaseHelper.COLUMN_STATUS));
        }
    }

    public synchronized void clear() {
        SQLiteDatabase database = databaseHelper.getWritableDatabase();
        database.delete(SessionDatabaseHelper.TABLE_SESSION, null, null);
    }

    @Nullable
    public synchronized String readToken() {
        SessionUser session = read();
        return session == null ? null : session.getToken();
    }

    private static String getString(Cursor cursor, String column) {
        return cursor.getString(cursor.getColumnIndexOrThrow(column));
    }
}

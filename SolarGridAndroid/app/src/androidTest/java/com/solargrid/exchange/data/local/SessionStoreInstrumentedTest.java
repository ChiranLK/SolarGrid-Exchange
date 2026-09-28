package com.solargrid.exchange.data.local;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNull;

import android.content.Context;
import android.database.Cursor;
import android.database.sqlite.SQLiteDatabase;

import androidx.test.core.app.ApplicationProvider;
import androidx.test.ext.junit.runners.AndroidJUnit4;

import com.solargrid.exchange.data.model.SessionUser;

import org.junit.After;
import org.junit.Before;
import org.junit.Test;
import org.junit.runner.RunWith;

/** Real SQLite behaviour of the single-row session cache used by Member 1 and shared by Members 2–4. */
@RunWith(AndroidJUnit4.class)
public final class SessionStoreInstrumentedTest {
    private SessionDatabaseHelper helper;
    private SessionStore store;

    @Before
    public void setUp() {
        Context context = ApplicationProvider.getApplicationContext();
        helper = new SessionDatabaseHelper(context);
        store = new SessionStore(helper);
        store.clear();
    }

    @After
    public void tearDown() {
        store.clear();
        helper.close();
    }

    @Test
    public void profileRefreshReplacesTheSingleRowAndKeepsTheServerToken() {
        store.save(new SessionUser("server-token", "200000000004", "Old Name", "old@example.com", "Prosumer", "Active"));
        SessionUser stored = store.read();

        store.save(stored.withProfile("New Name", "new@example.com", "Active"));

        SessionUser refreshed = store.read();
        assertEquals("server-token", refreshed.getToken());
        assertEquals("New Name", refreshed.getFullName());
        assertEquals("new@example.com", refreshed.getEmail());
        assertEquals("Prosumer", refreshed.getRole());
        SQLiteDatabase database = helper.getReadableDatabase();
        try (Cursor cursor = database.rawQuery("SELECT COUNT(*) FROM " + SessionDatabaseHelper.TABLE_SESSION, null)) {
            cursor.moveToFirst();
            assertEquals(1, cursor.getInt(0));
        }
    }

    @Test
    public void sessionTableNeverStoresPasswords() {
        SQLiteDatabase database = helper.getReadableDatabase();
        try (Cursor cursor = database.rawQuery("PRAGMA table_info(" + SessionDatabaseHelper.TABLE_SESSION + ")", null)) {
            while (cursor.moveToNext()) {
                String column = cursor.getString(cursor.getColumnIndexOrThrow("name")).toLowerCase();
                assertFalse(column + " must not hold a password", column.contains("password"));
            }
        }
    }

    @Test
    public void logoutClearsTheSession() {
        store.save(new SessionUser("server-token", "200000000004", "Name", "e@example.com", "Prosumer", "Active"));

        store.clear();

        assertNull(store.read());
        assertNull(store.readToken());
    }
}

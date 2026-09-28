package com.solargrid.exchange.features.transactions;

import java.util.regex.Pattern;

public final class QrTokenPolicy {
    private static final Pattern OPAQUE_TOKEN = Pattern.compile("^[A-Za-z0-9_-]{32,128}$");

    private QrTokenPolicy() { }

    public static boolean isSafeOpaqueToken(String value) {
        return value != null
                && OPAQUE_TOKEN.matcher(value).matches()
                && value.indexOf('.') < 0
                && value.indexOf(':') < 0
                && value.indexOf('@') < 0;
    }
}

package com.solargrid.exchange.features.operations;

import com.solargrid.exchange.data.model.SessionUser;

public final class RoleRoutePolicy {
    public enum HomeMode {
        PROSUMER,
        GRID_OPERATOR,
        PROFILE
    }

    private RoleRoutePolicy() {
    }

    public static HomeMode resolve(SessionUser session) {
        if (session.isProsumer()) {
            return HomeMode.PROSUMER;
        }
        if (session.isGridOperator()) {
            return HomeMode.GRID_OPERATOR;
        }
        return HomeMode.PROFILE;
    }
}

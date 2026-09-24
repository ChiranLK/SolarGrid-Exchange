package com.solargrid.exchange.core;

import android.content.Context;

import com.solargrid.exchange.data.local.SessionDatabaseHelper;
import com.solargrid.exchange.data.local.SessionStore;
import com.solargrid.exchange.features.auth.AuthRepository;
import com.solargrid.exchange.features.operations.OperatorTransactionRepository;
import com.solargrid.exchange.features.reservations.ReservationRepository;
import com.solargrid.exchange.features.stations.StationRepository;
import com.solargrid.exchange.features.users.UserRepository;
import com.solargrid.exchange.network.ApiClient;

public final class AppContainer {
    private final SessionStore sessionStore;
    private final ApiClient apiClient;
    private final AuthRepository authRepository;
    private final StationRepository stationRepository;
    private final UserRepository userRepository;
    private final ReservationRepository reservationRepository;
    private final OperatorTransactionRepository operatorTransactionRepository;

    public AppContainer(Context context) {
        Context applicationContext = context.getApplicationContext();
        SessionDatabaseHelper databaseHelper = new SessionDatabaseHelper(applicationContext);
        sessionStore = new SessionStore(databaseHelper);
        apiClient = new ApiClient(sessionStore);
        authRepository = new AuthRepository(apiClient, sessionStore);
        stationRepository = new StationRepository(apiClient);
        userRepository = new UserRepository(apiClient);
        reservationRepository = new ReservationRepository(apiClient);
        operatorTransactionRepository = new OperatorTransactionRepository(apiClient);
    }

    public SessionStore getSessionStore() {
        return sessionStore;
    }

    public AuthRepository getAuthRepository() {
        return authRepository;
    }

    public StationRepository getStationRepository() {
        return stationRepository;
    }

    public UserRepository getUserRepository() {
        return userRepository;
    }

    public ReservationRepository getReservationRepository() {
        return reservationRepository;
    }

    public OperatorTransactionRepository getOperatorTransactionRepository() {
        return operatorTransactionRepository;
    }
}

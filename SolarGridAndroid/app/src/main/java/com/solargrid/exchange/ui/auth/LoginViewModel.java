package com.solargrid.exchange.ui.auth;

import android.app.Application;

import androidx.annotation.NonNull;
import androidx.lifecycle.AndroidViewModel;
import androidx.lifecycle.LiveData;
import androidx.lifecycle.MutableLiveData;

import com.solargrid.exchange.SolarGridApplication;
import com.solargrid.exchange.data.model.SessionUser;
import com.solargrid.exchange.features.auth.AuthRepository;
import com.solargrid.exchange.network.ApiCallback;
import com.solargrid.exchange.network.ApiError;
import com.solargrid.exchange.ui.common.UiState;

public final class LoginViewModel extends AndroidViewModel {
    private final AuthRepository authRepository;
    private final MutableLiveData<UiState<SessionUser>> state =
            new MutableLiveData<>(UiState.idle());
    private boolean requestInFlight;

    public LoginViewModel(@NonNull Application application) {
        super(application);
        authRepository = ((SolarGridApplication) application).getAppContainer().getAuthRepository();
    }

    public LiveData<UiState<SessionUser>> getState() {
        return state;
    }

    public void login(String email, String password) {
        if (requestInFlight) {
            return;
        }
        requestInFlight = true;
        state.setValue(UiState.loading());
        authRepository.login(email, password, new ApiCallback<>() {
            @Override
            public void onSuccess(SessionUser value) {
                requestInFlight = false;
                state.setValue(UiState.success(value));
            }

            @Override
            public void onError(ApiError error) {
                requestInFlight = false;
                state.setValue(UiState.error(error));
            }
        });
    }
}

package com.solargrid.exchange.ui.common;

import androidx.annotation.Nullable;

import com.solargrid.exchange.network.ApiError;

public final class UiState<T> {
    public enum Status { IDLE, LOADING, SUCCESS, EMPTY, ERROR }

    private final Status status;
    private final T data;
    private final ApiError error;

    private UiState(Status status, @Nullable T data, @Nullable ApiError error) {
        this.status = status;
        this.data = data;
        this.error = error;
    }

    public static <T> UiState<T> idle() { return new UiState<>(Status.IDLE, null, null); }
    public static <T> UiState<T> loading() { return new UiState<>(Status.LOADING, null, null); }
    public static <T> UiState<T> success(T data) { return new UiState<>(Status.SUCCESS, data, null); }
    public static <T> UiState<T> empty() { return new UiState<>(Status.EMPTY, null, null); }
    public static <T> UiState<T> error(ApiError error) { return new UiState<>(Status.ERROR, null, error); }

    public Status getStatus() { return status; }
    @Nullable public T getData() { return data; }
    @Nullable public ApiError getError() { return error; }
}

package com.solargrid.exchange.network;

public interface ApiCallback<T> {
    void onSuccess(T value);
    void onError(ApiError error);
}

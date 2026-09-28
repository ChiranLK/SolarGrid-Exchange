package com.solargrid.exchange.ui.operations;

import android.app.Application;

import androidx.annotation.NonNull;
import androidx.lifecycle.AndroidViewModel;
import androidx.lifecycle.LiveData;
import androidx.lifecycle.MutableLiveData;

import com.solargrid.exchange.SolarGridApplication;
import com.solargrid.exchange.data.model.CompletedTransaction;
import com.solargrid.exchange.data.model.VerifiedTransaction;
import com.solargrid.exchange.features.operations.OperatorFlowPolicy;
import com.solargrid.exchange.features.operations.OperatorTransactionRepository;
import com.solargrid.exchange.features.operations.TransactionErrorMapper;
import com.solargrid.exchange.network.ApiCallback;
import com.solargrid.exchange.network.ApiError;
import com.solargrid.exchange.ui.common.UiState;

public final class OperatorTransactionViewModel extends AndroidViewModel {
    private final OperatorTransactionRepository repository;
    private final MutableLiveData<UiState<VerifiedTransaction>> verificationState =
            new MutableLiveData<>(UiState.idle());
    private final MutableLiveData<UiState<CompletedTransaction>> completionState =
            new MutableLiveData<>(UiState.idle());
    private boolean verificationInFlight;
    private boolean completionInFlight;
    private int generation;

    public OperatorTransactionViewModel(@NonNull Application application) {
        super(application);
        repository = ((SolarGridApplication) application)
                .getAppContainer()
                .getOperatorTransactionRepository();
    }

    public LiveData<UiState<VerifiedTransaction>> getVerificationState() {
        return verificationState;
    }

    public LiveData<UiState<CompletedTransaction>> getCompletionState() {
        return completionState;
    }

    public boolean verifyScannedToken(String token) {
        UiState<VerifiedTransaction> current = verificationState.getValue();
        boolean hasVerified = current != null && current.getStatus() == UiState.Status.SUCCESS;
        if (!OperatorFlowPolicy.canAcceptScan(verificationInFlight, hasVerified)) {
            return false;
        }
        verificationInFlight = true;
        verificationState.setValue(UiState.loading());
        int requestGeneration = generation;
        repository.verify(token, new ApiCallback<>() {
            @Override
            public void onSuccess(VerifiedTransaction value) {
                if (requestGeneration != generation) {
                    return;
                }
                verificationInFlight = false;
                verificationState.setValue(UiState.success(value));
            }

            @Override
            public void onError(ApiError error) {
                if (requestGeneration != generation) {
                    return;
                }
                verificationInFlight = false;
                verificationState.setValue(UiState.error(
                        TransactionErrorMapper.toDisplayError(error)));
            }
        });
        return true;
    }

    public boolean complete(boolean confirmed) {
        UiState<VerifiedTransaction> verifiedState = verificationState.getValue();
        VerifiedTransaction verified = verifiedState == null ? null : verifiedState.getData();
        UiState<CompletedTransaction> completedState = completionState.getValue();
        boolean alreadyCompleted = completedState != null
                && completedState.getStatus() == UiState.Status.SUCCESS;
        if (!OperatorFlowPolicy.canSubmitCompletion(
                confirmed,
                verified != null,
                completionInFlight,
                alreadyCompleted)) {
            return false;
        }
        completionInFlight = true;
        completionState.setValue(UiState.loading());
        int requestGeneration = generation;
        repository.complete(verified, new ApiCallback<>() {
            @Override
            public void onSuccess(CompletedTransaction value) {
                if (requestGeneration != generation) {
                    return;
                }
                completionInFlight = false;
                completionState.setValue(UiState.success(value));
                verificationState.setValue(UiState.idle());
            }

            @Override
            public void onError(ApiError error) {
                if (requestGeneration != generation) {
                    return;
                }
                completionInFlight = false;
                completionState.setValue(UiState.error(
                        TransactionErrorMapper.toDisplayError(error)));
            }
        });
        return true;
    }

    public void prepareForScan() {
        generation++;
        verificationInFlight = false;
        completionInFlight = false;
        verificationState.setValue(UiState.idle());
        completionState.setValue(UiState.idle());
    }

    public void clearSensitiveState() {
        prepareForScan();
    }
}

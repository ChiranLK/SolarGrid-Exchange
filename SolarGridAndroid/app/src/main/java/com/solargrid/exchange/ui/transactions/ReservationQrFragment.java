package com.solargrid.exchange.ui.transactions;

import android.graphics.Bitmap;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.ImageView;
import android.widget.TextView;

import androidx.annotation.NonNull;
import androidx.annotation.Nullable;
import androidx.fragment.app.Fragment;
import androidx.lifecycle.ViewModelProvider;

import com.google.zxing.WriterException;
import com.solargrid.exchange.R;
import com.solargrid.exchange.SolarGridApplication;
import com.solargrid.exchange.data.model.QrTransactionToken;
import com.solargrid.exchange.data.model.Reservation;
import com.solargrid.exchange.data.model.SessionUser;
import com.solargrid.exchange.features.transactions.QrExpiry;
import com.solargrid.exchange.network.ApiError;
import com.solargrid.exchange.ui.MainActivity;
import com.solargrid.exchange.ui.common.UiState;
import com.solargrid.exchange.ui.common.UiStateView;
import com.solargrid.exchange.ui.reservations.ReservationFormatters;

public final class ReservationQrFragment extends Fragment {
    private final Handler countdownHandler = new Handler(Looper.getMainLooper());
    private ReservationQrViewModel viewModel;
    private QrDisplayData displayed;
    private ImageView qrImage;
    private TextView countdown;
    private final Runnable countdownTick = new Runnable() {
        @Override
        public void run() {
            updateCountdown();
            if (displayed != null && QrExpiry.remainingSeconds(
                    displayed.getToken().getExpiresAtUtc(), System.currentTimeMillis()) > 0) {
                countdownHandler.postDelayed(this, 1_000L);
            }
        }
    };

    @Nullable
    @Override
    public View onCreateView(@NonNull LayoutInflater inflater, @Nullable ViewGroup container,
                             @Nullable Bundle savedInstanceState) {
        View view = inflater.inflate(R.layout.fragment_reservation_qr, container, false);
        View content = view.findViewById(R.id.reservation_qr_content);
        UiStateView stateView = view.findViewById(R.id.reservation_qr_state);
        qrImage = view.findViewById(R.id.reservation_qr_image);
        countdown = view.findViewById(R.id.reservation_qr_countdown);
        viewModel = new ViewModelProvider(this).get(ReservationQrViewModel.class);

        SessionUser session = ((SolarGridApplication) requireActivity().getApplication())
                .getAppContainer()
                .getAuthRepository()
                .getStoredSession();
        if (session == null) {
            ((MainActivity) requireActivity()).handleAuthenticationExpiry();
            return view;
        }
        if (!session.isProsumer()) {
            stateView.showError(new ApiError(
                    ApiError.Kind.FORBIDDEN, 403, getString(R.string.qr_prosumer_only)), null);
            return view;
        }

        view.findViewById(R.id.reservation_qr_reissue)
                .setOnClickListener(ignored -> viewModel.reissue());
        viewModel.getState().observe(getViewLifecycleOwner(), state -> {
            content.setVisibility(state.getStatus() == UiState.Status.SUCCESS
                    ? View.VISIBLE : View.GONE);
            switch (state.getStatus()) {
                case LOADING:
                    stopCountdown();
                    stateView.showLoading(getString(R.string.loading_qr));
                    break;
                case ERROR:
                    stopCountdown();
                    displayed = null;
                    if (state.getError() != null && state.getError().isAuthenticationExpired()) {
                        ((MainActivity) requireActivity()).handleAuthenticationExpiry();
                    } else if (state.getError() != null) {
                        stateView.showError(state.getError(), ignored -> viewModel.reissue());
                    }
                    break;
                case SUCCESS:
                    stateView.hide();
                    displayed = state.getData();
                    bind(view, displayed);
                    startCountdown();
                    break;
                default:
                    break;
            }
        });

        String reservationId = getArguments() == null
                ? ""
                : getArguments().getString("reservationId", "");
        viewModel.load(reservationId);
        return view;
    }

    @Override
    public void onStart() {
        super.onStart();
        startCountdown();
    }

    @Override
    public void onStop() {
        stopCountdown();
        super.onStop();
    }

    @Override
    public void onDestroyView() {
        stopCountdown();
        if (qrImage != null) {
            qrImage.setImageBitmap(null);
        }
        qrImage = null;
        countdown = null;
        super.onDestroyView();
    }

    private void bind(View view, QrDisplayData data) {
        if (data == null) {
            return;
        }
        Reservation reservation = data.getReservation();
        QrTransactionToken token = data.getToken();
        ((TextView) view.findViewById(R.id.reservation_qr_summary)).setText(getString(
                R.string.qr_reservation_summary,
                ReservationFormatters.displayReference(reservation.getId()),
                ReservationFormatters.station(
                        reservation.getStationName(), reservation.getStationId()),
                com.solargrid.exchange.ui.common.DisplayFormats.slotLabel(
                        reservation.getScheduledStartTimeUtc(), reservation.getScheduledEndTimeUtc()),
                ReservationFormatters.energy(reservation.getRequestedEnergyKwh())));
        try {
            Bitmap bitmap = QrBitmapEncoder.encode(token.getQrToken(), 640);
            qrImage.setImageBitmap(bitmap);
        } catch (WriterException exception) {
            qrImage.setImageBitmap(null);
            countdown.setText(R.string.qr_render_failed);
        }
    }

    private void startCountdown() {
        stopCountdown();
        if (displayed != null && countdown != null) {
            countdownTick.run();
        }
    }

    private void stopCountdown() {
        countdownHandler.removeCallbacks(countdownTick);
    }

    private void updateCountdown() {
        if (displayed == null || countdown == null || qrImage == null) {
            return;
        }
        long seconds = QrExpiry.remainingSeconds(
                displayed.getToken().getExpiresAtUtc(), System.currentTimeMillis());
        if (seconds <= 0) {
            qrImage.setImageBitmap(null);
            countdown.setText(R.string.qr_expired);
            return;
        }
        countdown.setText(getString(
                R.string.qr_expires_countdown,
                seconds / 60,
                seconds % 60));
    }
}

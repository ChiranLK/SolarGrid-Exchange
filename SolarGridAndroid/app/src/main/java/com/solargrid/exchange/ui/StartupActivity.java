package com.solargrid.exchange.ui;

import android.animation.Animator;
import android.animation.AnimatorSet;
import android.animation.ObjectAnimator;
import android.animation.ValueAnimator;
import android.content.Intent;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.os.SystemClock;
import android.view.View;
import android.view.animation.DecelerateInterpolator;
import android.view.animation.LinearInterpolator;
import android.view.animation.OvershootInterpolator;

import androidx.appcompat.app.AppCompatActivity;
import androidx.lifecycle.ViewModelProvider;

import com.solargrid.exchange.R;
import com.solargrid.exchange.SolarGridApplication;
import com.solargrid.exchange.features.auth.AccountRoutePolicy;
import com.solargrid.exchange.features.auth.AuthRepository;
import com.solargrid.exchange.network.ApiError;
import com.solargrid.exchange.ui.auth.AccountNavigator;
import com.solargrid.exchange.ui.common.UiPreferences;
import com.solargrid.exchange.ui.common.UiState;
import com.solargrid.exchange.ui.common.UiStateView;
import com.solargrid.exchange.ui.onboarding.OnboardingActivity;

import java.util.ArrayList;
import java.util.List;

public final class StartupActivity extends AppCompatActivity {
    /** Minimum time the animated splash stays visible so the intro can finish. */
    private static final long MIN_SPLASH_MS = 1_600L;

    private boolean routed;
    private long shownAt;
    private final Handler handler = new Handler(Looper.getMainLooper());
    private final List<Animator> loops = new ArrayList<>();

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_startup);
        shownAt = SystemClock.elapsedRealtime();
        playIntro();
        View loading = findViewById(R.id.startup_loading);
        UiStateView stateView = findViewById(R.id.startup_state);
        AuthRepository authRepository =
                ((SolarGridApplication) getApplication()).getAppContainer().getAuthRepository();
        StartupViewModel viewModel = new ViewModelProvider(this).get(StartupViewModel.class);
        viewModel.getState().observe(this, state -> {
            if (routed) {
                return;
            }
            loading.setVisibility(state.getStatus() == UiState.Status.LOADING ||
                    state.getStatus() == UiState.Status.IDLE ? View.VISIBLE : View.GONE);
            if (state.getStatus() == UiState.Status.SUCCESS && state.getData() != null) {
                routed = true;
                if (AccountRoutePolicy.afterAuthentication(state.getData())
                        == AccountRoutePolicy.Destination.BACKOFFICE_WEB_ONLY) {
                    authRepository.logout();
                    afterSplash(() -> AccountNavigator.toBackofficeWebOnly(this));
                } else {
                    afterSplash(() -> AccountNavigator.toMainApp(this));
                }
            } else if (state.getStatus() == UiState.Status.ERROR && state.getError() != null) {
                handleSessionFailure(state.getError(), authRepository, stateView, viewModel);
            } else {
                stateView.hide();
            }
        });
        viewModel.restoreSession();
    }

    private void handleSessionFailure(ApiError error, AuthRepository authRepository,
                                      UiStateView stateView, StartupViewModel viewModel) {
        switch (AccountRoutePolicy.afterSessionFailure(error)) {
            case SIGN_IN:
                routed = true;
                // First launch on this device: show the feature onboarding before sign-in.
                afterSplash(() -> {
                    if (UiPreferences.isOnboardingDone(this)) {
                        AccountNavigator.toSignIn(this, null);
                    } else {
                        Intent intent = new Intent(this, OnboardingActivity.class);
                        intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TASK);
                        startActivity(intent);
                        overridePendingTransition(android.R.anim.fade_in, android.R.anim.fade_out);
                        finish();
                    }
                });
                break;
            case PENDING_ACTIVATION:
                routed = true;
                authRepository.logout();
                afterSplash(() -> AccountNavigator.toPendingActivation(this, null));
                break;
            case SIGN_IN_WITH_ACCOUNT_NOTICE:
                // The stored token is still valid, but the account was deactivated since sign-in.
                routed = true;
                authRepository.logout();
                String notice = getString(R.string.account_no_longer_active, error.getMessage());
                afterSplash(() -> AccountNavigator.toSignIn(this, notice));
                break;
            default:
                // Offline or server problem: keep the session and offer a retry.
                stateView.showError(error, ignored -> viewModel.restoreSession());
                break;
        }
    }

    /** Runs a route once the intro has had its minimum time on screen. Routing itself is unchanged. */
    private void afterSplash(Runnable route) {
        long remaining = MIN_SPLASH_MS - (SystemClock.elapsedRealtime() - shownAt);
        handler.postDelayed(() -> {
            if (!isFinishing() && !isDestroyed()) {
                route.run();
            }
        }, Math.max(0L, remaining));
    }

    private void playIntro() {
        View rings = findViewById(R.id.startup_rings);
        View glow = findViewById(R.id.startup_glow);
        View logo = findViewById(R.id.startup_logo);
        View title = findViewById(R.id.startup_title);
        View tagline = findViewById(R.id.startup_tagline);
        View status = findViewById(R.id.startup_status);

        rings.setAlpha(0f);
        rings.setScaleX(0.82f);
        rings.setScaleY(0.82f);
        logo.setAlpha(0f);
        logo.setScaleX(0.5f);
        logo.setScaleY(0.5f);
        glow.setAlpha(0f);
        for (View view : new View[]{title, tagline, status}) {
            view.setAlpha(0f);
            view.setTranslationY(dp(24));
        }

        rings.animate().alpha(1f).scaleX(1f).scaleY(1f).setDuration(900)
                .setInterpolator(new DecelerateInterpolator()).start();
        logo.animate().alpha(1f).scaleX(1f).scaleY(1f).setStartDelay(150).setDuration(700)
                .setInterpolator(new OvershootInterpolator(1.6f)).start();
        glow.animate().alpha(1f).setStartDelay(400).setDuration(600).start();
        long delay = 380;
        for (View view : new View[]{title, tagline, status}) {
            view.animate().alpha(1f).translationY(0f).setStartDelay(delay).setDuration(520)
                    .setInterpolator(new DecelerateInterpolator(1.5f)).start();
            delay += 140;
        }

        // Continuous, gentle motion while the session is checked.
        ObjectAnimator spin = ObjectAnimator.ofFloat(rings, View.ROTATION, 0f, 360f);
        spin.setDuration(60_000);
        spin.setRepeatCount(ValueAnimator.INFINITE);
        spin.setInterpolator(new LinearInterpolator());
        spin.start();
        loops.add(spin);

        AnimatorSet pulse = new AnimatorSet();
        ObjectAnimator glowX = ObjectAnimator.ofFloat(glow, View.SCALE_X, 0.85f, 1.12f);
        ObjectAnimator glowY = ObjectAnimator.ofFloat(glow, View.SCALE_Y, 0.85f, 1.12f);
        for (ObjectAnimator animator : new ObjectAnimator[]{glowX, glowY}) {
            animator.setRepeatCount(ValueAnimator.INFINITE);
            animator.setRepeatMode(ValueAnimator.REVERSE);
            animator.setDuration(1_600);
        }
        pulse.playTogether(glowX, glowY);
        pulse.setStartDelay(900);
        pulse.start();
        loops.add(pulse);
    }

    private float dp(int value) {
        return value * getResources().getDisplayMetrics().density;
    }

    @Override
    protected void onDestroy() {
        handler.removeCallbacksAndMessages(null);
        for (Animator animator : loops) {
            animator.cancel();
        }
        super.onDestroy();
    }
}

package com.solargrid.exchange.ui.common;

import android.content.Context;
import android.content.res.ColorStateList;
import android.util.AttributeSet;
import android.view.LayoutInflater;
import android.view.View;
import android.widget.Button;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.TextView;

import androidx.annotation.ColorRes;
import androidx.annotation.DrawableRes;
import androidx.annotation.Nullable;
import androidx.core.content.ContextCompat;
import androidx.core.widget.ImageViewCompat;

import com.solargrid.exchange.R;
import com.solargrid.exchange.network.ApiError;

public final class UiStateView extends LinearLayout {
    private ProgressBar progress;
    private TextView title;
    private TextView message;
    private Button retry;
    private View iconFrame;
    private ImageView icon;

    public UiStateView(Context context) {
        this(context, null);
    }

    public UiStateView(Context context, @Nullable AttributeSet attrs) {
        super(context, attrs);
        setOrientation(VERTICAL);
        LayoutInflater.from(context).inflate(R.layout.view_ui_state, this, true);
        progress = findViewById(R.id.state_progress);
        title = findViewById(R.id.state_title);
        message = findViewById(R.id.state_message);
        retry = findViewById(R.id.state_retry);
        iconFrame = findViewById(R.id.state_icon_frame);
        icon = findViewById(R.id.state_icon);
    }

    public void showLoading(String loadingMessage) {
        setVisibility(VISIBLE);
        progress.setVisibility(VISIBLE);
        iconFrame.setVisibility(GONE);
        title.setText(R.string.loading_title);
        message.setText(loadingMessage);
        retry.setVisibility(GONE);
    }

    public void showEmpty(String emptyTitle, String emptyMessage) {
        showEmpty(emptyTitle, emptyMessage, null);
    }

    public void showEmpty(
            String emptyTitle,
            String emptyMessage,
            @Nullable OnClickListener retryListener) {
        showEmpty(emptyTitle, emptyMessage, getContext().getString(R.string.retry), retryListener);
    }

    public void showEmpty(
            String emptyTitle,
            String emptyMessage,
            String actionLabel,
            @Nullable OnClickListener retryListener) {
        setVisibility(VISIBLE);
        progress.setVisibility(GONE);
        showIcon(R.drawable.sg_ic_sun, R.color.sg_primary_container, R.color.sg_primary);
        title.setText(emptyTitle);
        message.setText(emptyMessage);
        retry.setVisibility(retryListener == null ? GONE : VISIBLE);
        retry.setText(actionLabel);
        retry.setOnClickListener(retryListener);
    }

    public void showError(ApiError error, @Nullable OnClickListener retryListener) {
        setVisibility(VISIBLE);
        progress.setVisibility(GONE);
        showErrorIcon(error);
        title.setText(titleFor(error));
        message.setText(error.getMessage());
        retry.setVisibility(retryListener == null ? GONE : VISIBLE);
        retry.setText(R.string.retry);
        retry.setOnClickListener(retryListener);
    }

    public void hide() {
        setVisibility(View.GONE);
    }

    // Presentation only: the decorative tile mirrors the state already chosen by the caller.
    private void showErrorIcon(ApiError error) {
        switch (error.getKind()) {
            case NETWORK:
                showIcon(R.drawable.sg_ic_wifi, R.color.sg_primary_container, R.color.sg_primary);
                break;
            case UNAUTHORIZED:
                showIcon(R.drawable.sg_ic_lock, R.color.sg_pending_container, R.color.sg_on_pending_container);
                break;
            case FORBIDDEN:
                showIcon(R.drawable.sg_ic_shield, R.color.sg_error_container, R.color.sg_on_error_container);
                break;
            default:
                showIcon(R.drawable.sg_ic_alert, R.color.sg_error_container, R.color.sg_on_error_container);
                break;
        }
    }

    private void showIcon(@DrawableRes int drawable, @ColorRes int tile, @ColorRes int tint) {
        Context context = getContext();
        icon.setImageResource(drawable);
        icon.setBackgroundTintList(ColorStateList.valueOf(ContextCompat.getColor(context, tile)));
        ImageViewCompat.setImageTintList(icon, ColorStateList.valueOf(ContextCompat.getColor(context, tint)));
        iconFrame.setVisibility(VISIBLE);
    }

    private String titleFor(ApiError error) {
        switch (error.getKind()) {
            case VALIDATION:
                return getContext().getString(R.string.validation_error_title);
            case UNAUTHORIZED:
                return getContext().getString(R.string.unauthorized_title);
            case FORBIDDEN:
                return getContext().getString(R.string.forbidden_title);
            case CONFLICT:
                return getContext().getString(R.string.conflict_title);
            case NETWORK:
                return getContext().getString(R.string.network_error_title);
            default:
                return getContext().getString(R.string.error_title);
        }
    }
}

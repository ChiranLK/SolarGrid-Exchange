package com.solargrid.exchange.ui.common;

import android.content.Context;
import android.util.AttributeSet;
import android.view.LayoutInflater;
import android.view.View;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.TextView;

import androidx.annotation.Nullable;

import com.solargrid.exchange.R;
import com.solargrid.exchange.network.ApiError;

public final class UiStateView extends LinearLayout {
    private ProgressBar progress;
    private TextView title;
    private TextView message;
    private Button retry;

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
    }

    public void showLoading(String loadingMessage) {
        setVisibility(VISIBLE);
        progress.setVisibility(VISIBLE);
        title.setText(R.string.loading_title);
        message.setText(loadingMessage);
        retry.setVisibility(GONE);
    }

    public void showEmpty(String emptyTitle, String emptyMessage) {
        setVisibility(VISIBLE);
        progress.setVisibility(GONE);
        title.setText(emptyTitle);
        message.setText(emptyMessage);
        retry.setVisibility(GONE);
    }

    public void showError(ApiError error, @Nullable OnClickListener retryListener) {
        setVisibility(VISIBLE);
        progress.setVisibility(GONE);
        title.setText(titleFor(error));
        message.setText(error.getMessage());
        retry.setVisibility(retryListener == null ? GONE : VISIBLE);
        retry.setOnClickListener(retryListener);
    }

    public void hide() {
        setVisibility(View.GONE);
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

package com.solargrid.exchange.ui.common;

import android.content.Context;
import android.content.res.ColorStateList;
import android.widget.TextView;

import androidx.annotation.ColorRes;
import androidx.annotation.DrawableRes;
import androidx.core.content.ContextCompat;
import androidx.core.widget.TextViewCompat;

import com.solargrid.exchange.R;

/**
 * Presentation-only status pill styling. Maps the raw API status value (never translated text)
 * to a semantic background and foreground. Always resets, so recycled rows never keep a stale
 * colour. Business decisions about status remain with the API and the existing view models.
 */
public final class StatusStyles {
    private StatusStyles() { }

    public static void apply(TextView view, String status) {
        @DrawableRes int background;
        @ColorRes int foreground;
        String value = status == null ? "" : status.trim();
        switch (value) {
            case "Pending":
            case "PendingActivation":
                background = R.drawable.sg_bg_status_pending;
                foreground = R.color.sg_on_pending_container;
                break;
            case "Approved":
            case "Active":
            case "Available":
                background = R.drawable.sg_bg_status_approved;
                foreground = R.color.sg_on_approved_container;
                break;
            case "Rejected":
            case "Unavailable":
                background = R.drawable.sg_bg_status_rejected;
                foreground = R.color.sg_on_rejected_container;
                break;
            case "Completed":
                background = R.drawable.sg_bg_status_completed;
                foreground = R.color.sg_on_completed_container;
                break;
            default:
                // Cancelled, Deactivated, FullyBooked and unknown values use the neutral pill.
                background = R.drawable.sg_bg_status_cancelled;
                foreground = R.color.sg_on_cancelled_container;
                break;
        }
        Context context = view.getContext();
        int color = ContextCompat.getColor(context, foreground);
        view.setBackgroundResource(background);
        view.setTextColor(color);
        TextViewCompat.setCompoundDrawableTintList(view, ColorStateList.valueOf(color));
    }
}

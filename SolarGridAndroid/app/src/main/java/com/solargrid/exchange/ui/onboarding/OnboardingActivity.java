package com.solargrid.exchange.ui.onboarding;

import android.animation.ArgbEvaluator;
import android.animation.ObjectAnimator;
import android.animation.ValueAnimator;
import android.content.res.ColorStateList;
import android.os.Bundle;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.view.animation.DecelerateInterpolator;
import android.view.animation.LinearInterpolator;
import android.view.animation.OvershootInterpolator;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.TextView;

import androidx.activity.OnBackPressedCallback;
import androidx.annotation.ColorInt;
import androidx.annotation.DrawableRes;
import androidx.annotation.NonNull;
import androidx.annotation.StringRes;
import androidx.appcompat.app.AppCompatActivity;
import androidx.core.view.WindowCompat;
import androidx.core.view.WindowInsetsControllerCompat;
import androidx.core.widget.ImageViewCompat;
import androidx.recyclerview.widget.RecyclerView;
import androidx.viewpager2.widget.ViewPager2;

import com.google.android.material.button.MaterialButton;
import com.solargrid.exchange.R;
import com.solargrid.exchange.ui.auth.AccountNavigator;
import com.solargrid.exchange.ui.common.UiPreferences;

import java.util.ArrayList;
import java.util.List;

/**
 * First-run introduction (three pages) shown once per device before sign-in. Purely presentational:
 * it stores only the "seen" flag and then hands over to the existing sign-in screen.
 */
public final class OnboardingActivity extends AppCompatActivity {
    private static final Page[] PAGES = {
            new Page(0xFF0D3D2C, 0xFFF4C65A, R.drawable.sg_ic_pin,
                    R.string.sg_onboarding_1_eyebrow, R.string.sg_onboarding_1_title, R.string.sg_onboarding_1_body,
                    R.string.sg_onboarding_1_point_a, R.string.sg_onboarding_1_point_b,
                    R.string.sg_onboarding_1_chip_a, R.string.sg_onboarding_1_chip_b),
            new Page(0xFF0D3D2C, 0xFFF4C65A, R.drawable.sg_ic_calendar,
                    R.string.sg_onboarding_2_eyebrow, R.string.sg_onboarding_2_title, R.string.sg_onboarding_2_body,
                    R.string.sg_onboarding_2_point_a, R.string.sg_onboarding_2_point_b,
                    R.string.sg_onboarding_2_chip_a, R.string.sg_onboarding_2_chip_b),
            new Page(0xFF0D3D2C, 0xFFF4C65A, R.drawable.sg_ic_qr,
                    R.string.sg_onboarding_3_eyebrow, R.string.sg_onboarding_3_title, R.string.sg_onboarding_3_body,
                    R.string.sg_onboarding_3_point_a, R.string.sg_onboarding_3_point_b,
                    R.string.sg_onboarding_3_chip_a, R.string.sg_onboarding_3_chip_b),
    };

    private final ArgbEvaluator argb = new ArgbEvaluator();
    private final List<ValueAnimator> loops = new ArrayList<>();
    private ViewPager2 pager;
    private MaterialButton next;
    private LinearLayout indicator;
    private int lastAnimatedPage = -1;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_onboarding);
        WindowInsetsControllerCompat bars = WindowCompat.getInsetsController(getWindow(), getWindow().getDecorView());
        bars.setAppearanceLightStatusBars(false);
        bars.setAppearanceLightNavigationBars(false);

        View root = findViewById(R.id.onboarding_root);
        pager = findViewById(R.id.onboarding_pager);
        next = findViewById(R.id.onboarding_next);
        indicator = findViewById(R.id.onboarding_indicator);
        buildIndicator();

        pager.setAdapter(new PageAdapter());
        pager.setOffscreenPageLimit(1);
        pager.setPageTransformer((page, position) -> {
            // Parallax: art drifts slower than text, content fades as it leaves.
            View art = page.findViewById(R.id.onboarding_art);
            float abs = Math.abs(position);
            page.setAlpha(1f - Math.min(1f, abs * 0.9f));
            if (art != null) {
                art.setTranslationX(position * page.getWidth() * 0.35f);
                art.setRotation(position * -12f);
            }
        });
        pager.registerOnPageChangeCallback(new ViewPager2.OnPageChangeCallback() {
            @Override
            public void onPageScrolled(int position, float offset, int offsetPixels) {
                int from = PAGES[position].background;
                int to = PAGES[Math.min(position + 1, PAGES.length - 1)].background;
                root.setBackgroundColor((int) argb.evaluate(offset, from, to));
            }

            @Override
            public void onPageSelected(int position) {
                updateIndicator(position);
                boolean last = position == PAGES.length - 1;
                next.setText(last ? R.string.sg_onboarding_start : R.string.sg_onboarding_next);
                next.setBackgroundTintList(ColorStateList.valueOf(PAGES[position].accent));
                pager.post(() -> animatePage(position));
            }
        });

        next.setOnClickListener(ignored -> {
            int current = pager.getCurrentItem();
            if (current < PAGES.length - 1) {
                pager.setCurrentItem(current + 1, true);
            } else {
                finishOnboarding();
            }
        });
        findViewById(R.id.onboarding_skip).setOnClickListener(ignored -> finishOnboarding());
        getOnBackPressedDispatcher().addCallback(this, new OnBackPressedCallback(true) {
            @Override
            public void handleOnBackPressed() {
                if (pager.getCurrentItem() > 0) {
                    pager.setCurrentItem(pager.getCurrentItem() - 1, true);
                } else {
                    finishOnboarding();
                }
            }
        });

        float drift = 18 * getResources().getDisplayMetrics().density;
        floatForever(findViewById(R.id.onboarding_orb_one), drift, 5_200);
        floatForever(findViewById(R.id.onboarding_orb_two), -drift, 6_400);
    }

    private void finishOnboarding() {
        UiPreferences.markOnboardingDone(this);
        AccountNavigator.toSignIn(this, null);
        overridePendingTransition(android.R.anim.fade_in, android.R.anim.fade_out);
    }

    private void buildIndicator() {
        int size = (int) (8 * getResources().getDisplayMetrics().density);
        for (int index = 0; index < PAGES.length; index++) {
            View dot = new View(this);
            LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(size, size);
            params.setMarginEnd(size);
            dot.setLayoutParams(params);
            dot.setBackgroundResource(R.drawable.sg_onboarding_dot);
            indicator.addView(dot);
        }
        indicator.setContentDescription(getString(R.string.sg_onboarding_page_description, 1, PAGES.length));
        updateIndicator(0);
    }

    private void updateIndicator(int selected) {
        float density = getResources().getDisplayMetrics().density;
        for (int index = 0; index < indicator.getChildCount(); index++) {
            View dot = indicator.getChildAt(index);
            boolean active = index == selected;
            int target = (int) ((active ? 28 : 8) * density);
            ValueAnimator width = ValueAnimator.ofInt(dot.getLayoutParams().width, target);
            width.setDuration(260);
            width.addUpdateListener(animation -> {
                ViewGroup.LayoutParams params = dot.getLayoutParams();
                params.width = (int) animation.getAnimatedValue();
                dot.setLayoutParams(params);
            });
            width.start();
            dot.animate().alpha(active ? 1f : 0.4f).setDuration(260).start();
            dot.setBackgroundTintList(ColorStateList.valueOf(active ? PAGES[selected].accent : 0xFFFFFFFF));
        }
        indicator.setContentDescription(getString(R.string.sg_onboarding_page_description, selected + 1, PAGES.length));
    }

    /** Staggered entrance for the page that just became visible. */
    private void animatePage(int position) {
        if (position == lastAnimatedPage) {
            return;
        }
        lastAnimatedPage = position;
        RecyclerView list = (RecyclerView) pager.getChildAt(0);
        RecyclerView.ViewHolder holder = list.findViewHolderForAdapterPosition(position);
        if (holder == null) {
            return;
        }
        View page = holder.itemView;
        View tile = page.findViewById(R.id.onboarding_tile);
        tile.setScaleX(0.6f);
        tile.setScaleY(0.6f);
        tile.setRotation(-10f);
        tile.animate().scaleX(1f).scaleY(1f).rotation(0f).setDuration(650)
                .setInterpolator(new OvershootInterpolator(1.8f)).start();
        View ring = page.findViewById(R.id.onboarding_ring);
        ring.setRotation(-40f);
        ring.animate().rotation(0f).setDuration(900).setInterpolator(new DecelerateInterpolator()).start();

        int[] staggered = {R.id.onboarding_chip_one, R.id.onboarding_chip_two, R.id.onboarding_eyebrow,
                R.id.onboarding_title, R.id.onboarding_body, R.id.onboarding_point_one, R.id.onboarding_point_two};
        float shift = 28 * getResources().getDisplayMetrics().density;
        long delay = 120;
        for (int id : staggered) {
            View view = page.findViewById(id);
            view.setAlpha(0f);
            view.setTranslationY(shift);
            view.animate().alpha(1f).translationY(0f).setStartDelay(delay).setDuration(480)
                    .setInterpolator(new DecelerateInterpolator(1.6f)).start();
            delay += 70;
        }
    }

    private void floatForever(View view, float distance, long duration) {
        ObjectAnimator animator = ObjectAnimator.ofFloat(view, View.TRANSLATION_Y, -distance, distance);
        animator.setDuration(duration);
        animator.setRepeatCount(ValueAnimator.INFINITE);
        animator.setRepeatMode(ValueAnimator.REVERSE);
        animator.setInterpolator(new android.view.animation.AccelerateDecelerateInterpolator());
        animator.start();
        loops.add(animator);
        ObjectAnimator spin = ObjectAnimator.ofFloat(view, View.ROTATION, 0f, 360f);
        spin.setDuration(duration * 6);
        spin.setRepeatCount(ValueAnimator.INFINITE);
        spin.setInterpolator(new LinearInterpolator());
        spin.start();
        loops.add(spin);
    }

    @Override
    protected void onDestroy() {
        for (ValueAnimator animator : loops) {
            animator.cancel();
        }
        super.onDestroy();
    }

    private static final class Page {
        @ColorInt final int background;
        @ColorInt final int accent;
        @DrawableRes final int icon;
        @StringRes final int eyebrow;
        @StringRes final int title;
        @StringRes final int body;
        @StringRes final int pointOne;
        @StringRes final int pointTwo;
        @StringRes final int chipOne;
        @StringRes final int chipTwo;

        Page(int background, int accent, int icon, int eyebrow, int title, int body,
             int pointOne, int pointTwo, int chipOne, int chipTwo) {
            this.background = background;
            this.accent = accent;
            this.icon = icon;
            this.eyebrow = eyebrow;
            this.title = title;
            this.body = body;
            this.pointOne = pointOne;
            this.pointTwo = pointTwo;
            this.chipOne = chipOne;
            this.chipTwo = chipTwo;
        }
    }

    private static final class PageHolder extends RecyclerView.ViewHolder {
        PageHolder(View view) {
            super(view);
        }
    }

    private final class PageAdapter extends RecyclerView.Adapter<PageHolder> {
        @NonNull
        @Override
        public PageHolder onCreateViewHolder(@NonNull ViewGroup parent, int viewType) {
            View view = LayoutInflater.from(parent.getContext())
                    .inflate(R.layout.item_onboarding_page, parent, false);
            return new PageHolder(view);
        }

        @Override
        public void onBindViewHolder(@NonNull PageHolder holder, int position) {
            Page page = PAGES[position];
            View view = holder.itemView;
            ImageView icon = view.findViewById(R.id.onboarding_icon);
            icon.setImageResource(page.icon);
            ImageViewCompat.setImageTintList(icon, ColorStateList.valueOf(page.background));
            view.findViewById(R.id.onboarding_tile).setBackgroundTintList(ColorStateList.valueOf(page.accent));
            TextView eyebrow = view.findViewById(R.id.onboarding_eyebrow);
            eyebrow.setText(page.eyebrow);
            eyebrow.setTextColor(page.accent);
            ((TextView) view.findViewById(R.id.onboarding_title)).setText(page.title);
            ((TextView) view.findViewById(R.id.onboarding_body)).setText(page.body);
            ((TextView) view.findViewById(R.id.onboarding_point_one)).setText(page.pointOne);
            ((TextView) view.findViewById(R.id.onboarding_point_two)).setText(page.pointTwo);
            ((TextView) view.findViewById(R.id.onboarding_chip_one)).setText(page.chipOne);
            ((TextView) view.findViewById(R.id.onboarding_chip_two)).setText(page.chipTwo);
        }

        @Override
        public int getItemCount() {
            return PAGES.length;
        }
    }
}

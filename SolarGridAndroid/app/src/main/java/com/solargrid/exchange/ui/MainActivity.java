package com.solargrid.exchange.ui;

import android.content.Intent;
import android.os.Bundle;
import android.view.Menu;
import android.view.MenuItem;
import android.view.View;
import android.widget.TextView;

import androidx.annotation.NonNull;
import androidx.activity.OnBackPressedCallback;
import androidx.appcompat.app.ActionBarDrawerToggle;
import androidx.appcompat.app.AlertDialog;
import androidx.appcompat.app.AppCompatActivity;
import androidx.appcompat.widget.Toolbar;
import androidx.core.content.ContextCompat;
import androidx.core.view.GravityCompat;
import androidx.drawerlayout.widget.DrawerLayout;
import androidx.navigation.NavController;
import androidx.navigation.NavDestination;
import androidx.navigation.NavGraph;
import androidx.navigation.NavOptions;
import androidx.navigation.fragment.NavHostFragment;
import androidx.lifecycle.ViewModelProvider;

import com.google.android.material.bottomnavigation.BottomNavigationView;
import com.google.android.material.dialog.MaterialAlertDialogBuilder;
import com.google.android.material.navigation.NavigationView;
import com.solargrid.exchange.R;
import com.solargrid.exchange.SolarGridApplication;
import com.solargrid.exchange.data.model.SessionUser;
import com.solargrid.exchange.features.auth.AccountRoutePolicy;
import com.solargrid.exchange.features.auth.AuthRepository;
import com.solargrid.exchange.features.operations.RoleRoutePolicy;
import com.solargrid.exchange.network.ApiError;
import com.solargrid.exchange.ui.auth.AccountNavigator;
import com.solargrid.exchange.ui.auth.LoginActivity;
import com.solargrid.exchange.ui.common.DisplayFormats;
import com.solargrid.exchange.ui.common.UiPreferences;
import com.solargrid.exchange.ui.operations.OperatorTransactionViewModel;

public final class MainActivity extends AppCompatActivity
        implements NavigationView.OnNavigationItemSelectedListener {
    private DrawerLayout drawerLayout;
    private NavController navController;
    private AuthRepository authRepository;
    private BottomNavigationView bottomNav;
    private ActionBarDrawerToggle toggle;
    private String initials = "";
    private boolean topLevelShown = true;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        authRepository = ((SolarGridApplication) getApplication()).getAppContainer().getAuthRepository();
        SessionUser session = authRepository.getStoredSession();
        if (session == null) {
            routeToLogin();
            return;
        }
        if (AccountRoutePolicy.afterAuthentication(session) == AccountRoutePolicy.Destination.BACKOFFICE_WEB_ONLY) {
            // Backoffice administration is web-only; never open the mobile workspace for it.
            authRepository.logout();
            AccountNavigator.toBackofficeWebOnly(this);
            return;
        }

        setContentView(R.layout.activity_main);
        Toolbar toolbar = findViewById(R.id.main_toolbar);
        setSupportActionBar(toolbar);

        drawerLayout = findViewById(R.id.main_drawer);
        NavigationView navigationView = findViewById(R.id.main_navigation);
        navigationView.setNavigationItemSelectedListener(this);
        toggle = new ActionBarDrawerToggle(
                this,
                drawerLayout,
                toolbar,
                R.string.navigation_open,
                R.string.navigation_close);
        drawerLayout.addDrawerListener(toggle);
        toggle.syncState();
        toggle.setHomeAsUpIndicator(R.drawable.sg_ic_back);
        toggle.setToolbarNavigationClickListener(ignored -> getOnBackPressedDispatcher().onBackPressed());

        NavHostFragment host = (NavHostFragment) getSupportFragmentManager()
                .findFragmentById(R.id.main_nav_host);
        if (host == null) {
            throw new IllegalStateException("Navigation host is missing.");
        }
        navController = host.getNavController();
        NavGraph graph = navController.getNavInflater().inflate(R.navigation.main_nav_graph);
        graph.setStartDestination(startDestinationFor(session));
        navController.setGraph(graph);

        // Role-based bottom navigation (same destination IDs as the drawer).
        bottomNav = findViewById(R.id.main_bottom_nav);
        bottomNav.inflateMenu(session.isGridOperator()
                ? R.menu.bottom_nav_operator
                : R.menu.bottom_nav_prosumer);
        bottomNav.setOnItemSelectedListener(item -> {
            navigateTopLevel(item.getItemId());
            return true;
        });
        bottomNav.setOnItemReselectedListener(item -> navigateTopLevel(item.getItemId()));

        OperatorTransactionViewModel operatorFlow = new ViewModelProvider(this)
                .get(OperatorTransactionViewModel.class);
        navController.addOnDestinationChangedListener((controller, destination, arguments) -> {
            boolean topLevel = isTopLevel(destination.getId());
            toolbar.setTitle(topLevel ? getString(R.string.app_name) : destination.getLabel());
            // Presentation only: highlight the drawer entry for the visible destination.
            MenuItem current = navigationView.getMenu().findItem(destination.getId());
            if (current != null) {
                current.setChecked(true);
            }
            applyChrome(destination, topLevel);
            if (!isOperatorTransactionDestination(destination.getId())) {
                operatorFlow.clearSensitiveState();
            }
        });

        getOnBackPressedDispatcher().addCallback(this, new OnBackPressedCallback(true) {
            @Override
            public void handleOnBackPressed() {
                if (drawerLayout.isDrawerOpen(GravityCompat.START)) {
                    drawerLayout.closeDrawer(GravityCompat.START);
                } else if (!navController.popBackStack()) {
                    finish();
                }
            }
        });

        configureRoleNavigation(navigationView, session);
        TextView headerName = navigationView.getHeaderView(0).findViewById(R.id.nav_header_name);
        TextView headerRole = navigationView.getHeaderView(0).findViewById(R.id.nav_header_role);
        headerName.setText(session.getFullName());
        headerRole.setText(DisplayFormats.roleLabel(this, session.getRole()));
        initials = initialsFor(session.getFullName());
        TextView headerInitials = navigationView.getHeaderView(0).findViewById(R.id.nav_header_initials);
        headerInitials.setText(initials);
    }

    /** Toolbar and tab chrome for the visible destination. Navigation targets are unchanged. */
    private void applyChrome(NavDestination destination, boolean topLevel) {
        topLevelShown = topLevel;
        bottomNav.setVisibility(topLevel ? View.VISIBLE : View.GONE);
        MenuItem tab = bottomNav.getMenu().findItem(destination.getId());
        if (tab != null) {
            tab.setChecked(true);
        }
        toggle.setDrawerIndicatorEnabled(topLevel);
        if (getSupportActionBar() != null) {
            getSupportActionBar().setDisplayHomeAsUpEnabled(!topLevel);
            if (!topLevel) {
                getSupportActionBar().setHomeActionContentDescription(R.string.sg_navigate_back);
            }
        }
        drawerLayout.setDrawerLockMode(topLevel
                ? DrawerLayout.LOCK_MODE_UNLOCKED
                : DrawerLayout.LOCK_MODE_LOCKED_CLOSED);
        invalidateOptionsMenu();
    }

    private boolean isTopLevel(int destinationId) {
        return bottomNav != null && bottomNav.getMenu().findItem(destinationId) != null;
    }

    /** Switches tabs without stacking duplicate top-level screens. */
    private void navigateTopLevel(int destinationId) {
        NavDestination current = navController.getCurrentDestination();
        if (current != null && current.getId() == destinationId) {
            return;
        }
        int start = navController.getGraph().getStartDestinationId();
        if (destinationId == start) {
            navController.popBackStack(start, false);
            return;
        }
        NavOptions options = new NavOptions.Builder()
                .setLaunchSingleTop(true)
                .setPopUpTo(start, false)
                .build();
        navController.navigate(destinationId, null, options);
    }

    @Override
    public boolean onCreateOptionsMenu(Menu menu) {
        getMenuInflater().inflate(R.menu.main_toolbar_menu, menu);
        MenuItem avatarItem = menu.findItem(R.id.action_profile_avatar);
        View actionView = avatarItem.getActionView();
        if (actionView != null) {
            TextView avatar = actionView.findViewById(R.id.toolbar_avatar);
            avatar.setText(initials);
            avatar.setOnClickListener(ignored -> navigateTopLevel(R.id.nav_profile));
        }
        return true;
    }

    @Override
    public boolean onPrepareOptionsMenu(Menu menu) {
        menu.findItem(R.id.action_appearance).setVisible(topLevelShown);
        menu.findItem(R.id.action_profile_avatar).setVisible(topLevelShown);
        return super.onPrepareOptionsMenu(menu);
    }

    @Override
    public boolean onOptionsItemSelected(@NonNull MenuItem item) {
        if (item.getItemId() == R.id.action_appearance) {
            UiPreferences.showAppearancePicker(this);
            return true;
        }
        return super.onOptionsItemSelected(item);
    }

    @Override
    public boolean onNavigationItemSelected(@NonNull MenuItem item) {
        if (item.getItemId() == R.id.nav_sign_out) {
            drawerLayout.closeDrawer(GravityCompat.START);
            confirmSignOut();
            return false;
        }
        if (isTopLevel(item.getItemId())) {
            navigateTopLevel(item.getItemId());
        } else {
            navController.navigate(item.getItemId());
        }
        drawerLayout.closeDrawer(GravityCompat.START);
        return true;
    }

    /** Asks before ending the session; signing out itself is unchanged. */
    private void confirmSignOut() {
        AlertDialog dialog = new MaterialAlertDialogBuilder(this)
                .setIcon(R.drawable.sg_ic_logout)
                .setTitle(R.string.sg_sign_out_title)
                .setMessage(R.string.sg_sign_out_message)
                .setNegativeButton(R.string.sg_sign_out_stay, null)
                .setPositiveButton(R.string.sg_sign_out_confirm, (ignored, which) -> {
                    authRepository.logout();
                    routeToLogin();
                })
                .show();
        dialog.getButton(AlertDialog.BUTTON_POSITIVE)
                .setTextColor(ContextCompat.getColor(this, R.color.sg_error));
    }

    public void handleAuthenticationExpiry() {
        authRepository.logout();
        routeToLogin();
    }

    /**
     * Called by Member 1 screens when the API reports that this account is pending or has been
     * deactivated while the token was still valid. The local session is cleared either way.
     */
    public void handleAccountNoLongerActive(ApiError error) {
        authRepository.logout();
        if (AccountRoutePolicy.afterSessionFailure(error) == AccountRoutePolicy.Destination.PENDING_ACTIVATION) {
            AccountNavigator.toPendingActivation(this, null);
        } else {
            AccountNavigator.toSignIn(this, getString(R.string.account_no_longer_active, error.getMessage()));
        }
    }

    /**
     * Shared entry point for feature screens: ends the session for a 401 (expired/invalid token) or
     * an account-status 403 (pending/deactivated/not active) and returns true; returns false for
     * every other error so the screen shows it normally.
     */
    public boolean handleSessionFailure(ApiError error) {
        if (error == null) {
            return false;
        }
        switch (AccountRoutePolicy.afterSessionFailure(error)) {
            case SIGN_IN:
                handleAuthenticationExpiry();
                return true;
            case PENDING_ACTIVATION:
            case SIGN_IN_WITH_ACCOUNT_NOTICE:
                handleAccountNoLongerActive(error);
                return true;
            default:
                return false;
        }
    }

    private void configureRoleNavigation(NavigationView navigationView, SessionUser session) {
        Menu menu = navigationView.getMenu();
        menu.findItem(R.id.nav_prosumer_home).setVisible(session.isProsumer());
        menu.findItem(R.id.nav_my_reservations).setVisible(session.isProsumer());
        menu.findItem(R.id.nav_booking_history).setVisible(session.isProsumer());
        menu.findItem(R.id.nav_operator_home).setVisible(session.isGridOperator());
        menu.findItem(R.id.nav_qr_operations).setVisible(session.isGridOperator());
    }

    /** Display-only avatar initials; the session name itself is never modified. */
    static String initialsFor(String fullName) {
        return DisplayFormats.initials(fullName);
    }

    static int startDestinationFor(SessionUser session) {
        switch (RoleRoutePolicy.resolve(session)) {
            case PROSUMER:
                return R.id.nav_prosumer_home;
            case GRID_OPERATOR:
                return R.id.nav_operator_home;
            default:
                return R.id.nav_profile;
        }
    }

    private static boolean isOperatorTransactionDestination(int destinationId) {
        return destinationId == R.id.nav_qr_operations
                || destinationId == R.id.nav_transaction_verification
                || destinationId == R.id.nav_transaction_completion;
    }

    private void routeToLogin() {
        Intent intent = new Intent(this, LoginActivity.class);
        intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TASK);
        startActivity(intent);
        finish();
    }
}

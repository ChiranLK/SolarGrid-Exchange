package com.solargrid.exchange.ui;

import android.content.Intent;
import android.os.Bundle;
import android.view.Menu;
import android.view.MenuItem;
import android.widget.TextView;

import androidx.annotation.NonNull;
import androidx.activity.OnBackPressedCallback;
import androidx.appcompat.app.ActionBarDrawerToggle;
import androidx.appcompat.app.AppCompatActivity;
import androidx.appcompat.widget.Toolbar;
import androidx.core.view.GravityCompat;
import androidx.drawerlayout.widget.DrawerLayout;
import androidx.navigation.NavController;
import androidx.navigation.NavGraph;
import androidx.navigation.fragment.NavHostFragment;
import androidx.lifecycle.ViewModelProvider;

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
import com.solargrid.exchange.ui.operations.OperatorTransactionViewModel;

public final class MainActivity extends AppCompatActivity
        implements NavigationView.OnNavigationItemSelectedListener {
    private DrawerLayout drawerLayout;
    private NavController navController;
    private AuthRepository authRepository;

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
        ActionBarDrawerToggle toggle = new ActionBarDrawerToggle(
                this,
                drawerLayout,
                toolbar,
                R.string.navigation_open,
                R.string.navigation_close);
        drawerLayout.addDrawerListener(toggle);
        toggle.syncState();

        NavHostFragment host = (NavHostFragment) getSupportFragmentManager()
                .findFragmentById(R.id.main_nav_host);
        if (host == null) {
            throw new IllegalStateException("Navigation host is missing.");
        }
        navController = host.getNavController();
        NavGraph graph = navController.getNavInflater().inflate(R.navigation.main_nav_graph);
        graph.setStartDestination(startDestinationFor(session));
        navController.setGraph(graph);
        OperatorTransactionViewModel operatorFlow = new ViewModelProvider(this)
                .get(OperatorTransactionViewModel.class);
        navController.addOnDestinationChangedListener((controller, destination, arguments) -> {
            toolbar.setTitle(destination.getLabel());
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
        headerRole.setText(session.getRole());

    }

    @Override
    public boolean onNavigationItemSelected(@NonNull MenuItem item) {
        if (item.getItemId() == R.id.nav_sign_out) {
            authRepository.logout();
            routeToLogin();
            return true;
        }
        navController.navigate(item.getItemId());
        drawerLayout.closeDrawer(GravityCompat.START);
        return true;
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

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

import com.google.android.material.navigation.NavigationView;
import com.solargrid.exchange.R;
import com.solargrid.exchange.SolarGridApplication;
import com.solargrid.exchange.data.model.SessionUser;
import com.solargrid.exchange.features.auth.AuthRepository;
import com.solargrid.exchange.ui.auth.LoginActivity;

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
        graph.setStartDestination(session.isProsumer()
                ? R.id.nav_prosumer_home
                : R.id.nav_operator_home);
        navController.setGraph(graph);
        navController.addOnDestinationChangedListener((controller, destination, arguments) ->
                toolbar.setTitle(destination.getLabel()));

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

    private void configureRoleNavigation(NavigationView navigationView, SessionUser session) {
        Menu menu = navigationView.getMenu();
        menu.findItem(R.id.nav_prosumer_home).setVisible(session.isProsumer());
        menu.findItem(R.id.nav_my_reservations).setVisible(session.isProsumer());
        menu.findItem(R.id.nav_booking_history).setVisible(session.isProsumer());
        menu.findItem(R.id.nav_operator_home).setVisible(session.isStaff());
        menu.findItem(R.id.nav_qr_operations).setVisible(session.isStaff());
    }

    private void routeToLogin() {
        Intent intent = new Intent(this, LoginActivity.class);
        intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TASK);
        startActivity(intent);
        finish();
    }
}

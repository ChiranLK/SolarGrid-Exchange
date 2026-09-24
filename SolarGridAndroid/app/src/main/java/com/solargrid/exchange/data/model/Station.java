package com.solargrid.exchange.data.model;

public final class Station {
    private final String id;
    private final String name;
    private final String description;
    private final String address;
    private final double latitude;
    private final double longitude;
    private final double generationCapacityKw;
    private final double storageCapacityKwh;
    private final boolean active;

    public Station(String id, String name, String description, String address,
                   double latitude, double longitude, double generationCapacityKw,
                   double storageCapacityKwh, boolean active) {
        this.id = id;
        this.name = name;
        this.description = description;
        this.address = address;
        this.latitude = latitude;
        this.longitude = longitude;
        this.generationCapacityKw = generationCapacityKw;
        this.storageCapacityKwh = storageCapacityKwh;
        this.active = active;
    }

    public String getId() { return id; }
    public String getName() { return name; }
    public String getDescription() { return description; }
    public String getAddress() { return address; }
    public double getLatitude() { return latitude; }
    public double getLongitude() { return longitude; }
    public double getGenerationCapacityKw() { return generationCapacityKw; }
    public double getStorageCapacityKwh() { return storageCapacityKwh; }
    public boolean isActive() { return active; }

    @Override
    public String toString() {
        return name + "\n" + address;
    }
}

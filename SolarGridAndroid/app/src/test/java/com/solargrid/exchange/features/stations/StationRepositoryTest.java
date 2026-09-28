package com.solargrid.exchange.features.stations;

import static org.junit.Assert.assertEquals;

import com.solargrid.exchange.data.model.NearbyStation;

import org.json.JSONArray;
import org.junit.Test;

import java.util.List;

public final class StationRepositoryTest {
    @Test
    public void parsesNestedNearbyResponseAndKeepsLatitudeLongitudeOrder() throws Exception {
        JSONArray response = new JSONArray("[{\"station\":{\"id\":\"station-1\","
                + "\"name\":\"Solar One\",\"address\":\"Main Road\","
                + "\"latitude\":6.9271,\"longitude\":79.8612,\"isActive\":true},"
                + "\"distanceKm\":2.5}]");

        List<NearbyStation> nearby = StationRepository.parseNearbyStations(response);

        assertEquals(1, nearby.size());
        assertEquals("station-1", nearby.get(0).getStation().getId());
        assertEquals(6.9271, nearby.get(0).getStation().getLatitude(), 0.00001);
        assertEquals(79.8612, nearby.get(0).getStation().getLongitude(), 0.00001);
        assertEquals(2.5, nearby.get(0).getDistanceKm(), 0.00001);
    }

    @Test
    public void ignoresMalformedMarkerCoordinates() throws Exception {
        JSONArray response = new JSONArray("[{\"station\":{\"id\":\"bad\","
                + "\"latitude\":91,\"longitude\":0},\"distanceKm\":1}]");

        assertEquals(0, StationRepository.parseNearbyStations(response).size());
    }
}

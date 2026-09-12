-- ====================================================================
-- SPEC-03 / BE-61: SQL Migration & Data Validation Script
-- Kiểm tra tính hợp lệ của tọa độ Vĩ độ (Latitude) và Kinh độ (Longitude)
-- tại khu vực Thành phố Hồ Chí Minh (TP.HCM)
-- Giới hạn hợp lệ TP.HCM: Latitude [10.3 - 11.2], Longitude [106.3 - 107.1]
-- Trọng tâm trung tâm TP.HCM: Latitude [10.7 - 10.9], Longitude [106.6 - 106.8]
-- ====================================================================

-- 1. Thêm Constraint kiểm tra tọa độ nếu bảng PLACES đã tồn tại
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'places') THEN
        -- Check Latitude Constraint
        IF NOT EXISTS (SELECT 1 FROM information_schema.table_constraints WHERE constraint_name = 'chk_places_latitude') THEN
            ALTER TABLE places ADD CONSTRAINT chk_places_latitude CHECK (latitude BETWEEN 10.3 AND 11.2);
        END IF;

        -- Check Longitude Constraint
        IF NOT EXISTS (SELECT 1 FROM information_schema.table_constraints WHERE constraint_name = 'chk_places_longitude') THEN
            ALTER TABLE places ADD CONSTRAINT chk_places_longitude CHECK (longitude BETWEEN 106.3 AND 107.1);
        END IF;
    END IF;
END $$;

-- 2. Truy vấn báo cáo tọa độ bất hợp lệ (Out of bounds TP.HCM)
SELECT 
    id, 
    name, 
    latitude, 
    longitude,
    CASE 
        WHEN latitude IS NULL OR longitude IS NULL THEN 'Missing Coordinates'
        WHEN latitude < 10.3 OR latitude > 11.2 THEN 'Latitude Out of HCMC Range (10.3 - 11.2)'
        WHEN longitude < 106.3 OR longitude > 107.1 THEN 'Longitude Out of HCMC Range (106.3 - 107.1)'
        ELSE 'Valid HCMC Coordinate'
    END AS validation_status
FROM places
WHERE latitude IS NULL 
   OR longitude IS NULL 
   OR latitude < 10.3 OR latitude > 11.2 
   OR longitude < 106.3 OR longitude > 107.1;

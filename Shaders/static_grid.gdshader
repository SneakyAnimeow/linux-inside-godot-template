shader_type spatial;

uniform vec3 line_color : source_color = vec3(1.0, 1.0, 0.5);
uniform vec3 base_color : source_color = vec3(0.02, 0.02, 0.05);
uniform float grid_size = 12.0;
uniform float line_thickness = 0.5;
uniform float glow = 20.0;

varying vec3 world_pos;

void vertex() {
    // Capture the absolute world position of the floor
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
    // Use the world position for the UVs instead of local UVs or TIME.
    // This perfectly anchors the grid to the world, making player movement obvious!
    vec2 uv = world_pos.xz / grid_size;
    
    vec2 grid = fract(uv);
    vec2 dist = abs(grid - 0.5);
    
    float line_x = smoothstep(0.5 - line_thickness, 0.5, dist.x);
    float line_y = smoothstep(0.5 - line_thickness, 0.5, dist.y);
    
    float grid_mask = max(line_x, line_y);
    
    ALBEDO = mix(base_color, line_color, grid_mask);
    EMISSION = line_color * grid_mask * glow;
    
    METALLIC = 0.2;
    ROUGHNESS = 0.8;
}

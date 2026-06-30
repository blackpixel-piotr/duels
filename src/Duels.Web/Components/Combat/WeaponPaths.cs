namespace Duels.Web.Components.Combat;

public static class WeaponPaths
{
    public static string ForWeapon(string weaponId) => weaponId switch
    {
        "dragon_dagger"      => "M 200 100 C 260 80 320 120 300 160 C 280 200 220 200 200 150 C 180 100 120 100 100 150 C 80 200 120 220 180 200 C 240 180 260 140 200 100",
        "dragon_claws"       => "M 90 70 L 150 190 M 150 70 L 210 190 M 210 70 L 270 190 M 270 70 L 330 190",
        "armadyl_godsword"   => "M 80 260 Q 160 120 260 80 Q 330 60 310 160 Q 295 220 240 240",
        "bandos_godsword"    => "M 200 60 Q 260 100 280 180 Q 290 240 200 270 Q 110 240 120 160 Q 140 80 200 60",
        "zamorak_godsword"   => "M 200 60 Q 260 100 280 180 Q 290 240 200 270 Q 110 240 120 160 Q 140 80 200 60",
        "saradomin_godsword" => "M 120 200 Q 160 120 200 80 Q 240 120 280 200",
        "scythe_of_vitur"    => "M 40 140 C 140 40 260 260 360 140",
        "abyssal_whip"       => "M 200 150 C 260 100 310 130 290 170 C 270 210 220 200 200 150 C 180 100 220 60 260 90",
        "rune_scimitar"      => "M 80 80 Q 200 50 320 200",
        "dragon_scimitar"    => "M 80 80 Q 200 50 320 200",
        "armadyl_sword"      => "M 100 60 L 300 240",
        _                    => "M 80 200 Q 200 60 320 200",
    };

    public static string LabelForWeapon(string weaponId) => weaponId switch
    {
        "dragon_dagger"      => "TRACE THE INFINITY",
        "dragon_claws"       => "FOUR SLASHES",
        "armadyl_godsword"   => "POWER ARC",
        "bandos_godsword"    => "OVERHEAD SLAM",
        "zamorak_godsword"   => "OVERHEAD SLAM",
        "saradomin_godsword" => "DIVINE ARC",
        "scythe_of_vitur"    => "WIDE SWEEP",
        "abyssal_whip"       => "WHIP COIL",
        _                    => "TRACE THE PATH",
    };
}

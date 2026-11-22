-- Tuyệt chiêu của Ignis: Gây sát thương lớn, nếu máu bản thân dưới 50% tự hồi 25% HP
function on_cast(ctx)
    local caster = ctx:get_caster()
    local target = ctx:get_target()

    local raw_power = math.floor(caster.atk * 1.65)
    ctx:deal_damage(target, raw_power, "Phượng Hoàng Tái Sinh")

    if caster.hp < (caster.max_hp * 0.5) then
        local heal_amount = math.floor(caster.max_hp * 0.25)
        ctx:heal(caster, heal_amount, "Ngọn Lửa Hồi Sinh")
    end
end

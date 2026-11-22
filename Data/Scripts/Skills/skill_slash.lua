-- Kỹ năng cơ bản của Arthur: Chém thường + chí mạng nếu mục tiêu còn trên 50% máu
function on_cast(ctx)
    local caster = ctx:get_caster()
    local target = ctx:get_target()
    local multiplier = 1.25

    if target.hp > (target.max_hp * 0.5) then
        multiplier = 1.45
    end

    local raw_power = math.floor(caster.atk * multiplier)
    ctx:deal_damage(target, raw_power, "Chém Ánh Sáng")
end

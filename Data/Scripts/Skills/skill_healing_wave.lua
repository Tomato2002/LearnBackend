-- Tuyệt chiêu của Lyra: Hồi máu cho đồng đội thấp máu nhất (35% MaxHP của Lyra)
function on_cast(ctx)
    local caster = ctx:get_caster()
    local ally = ctx:get_lowest_hp_ally()

    local heal_amount = math.floor(caster.max_hp * 0.35)
    ctx:heal(ally, heal_amount, "Sóng Thủy Triều Hồi Máu")
end

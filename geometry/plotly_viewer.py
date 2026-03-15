from typing import Dict, List, Optional

import plotly.graph_objects as go

from geometry.model import Model, RadialBuilding


def plot_model_3d(model: Model, show_node_ids: bool = False) -> go.Figure:
    colors = {
        "core ring": "#1f77b4",
        "inner ring": "#2ca02c",
        "middle ring": "#ff7f0e",
        "outer ring": "#d62728",
        "nerve": "#7f7f7f",
        "column": "#111111",
    }

    fig = go.Figure()
    grouped_coords: Dict[str, Dict[str, List[Optional[float]]]] = {}

    for line in model.lines.values():
        ni = model.nodes[line["Ni"]]
        nj = model.nodes[line["Nj"]]
        coords = grouped_coords.setdefault(line["type"], {"x": [], "y": [], "z": []})
        coords["x"].extend([ni["x"], nj["x"], None])
        coords["y"].extend([ni["y"], nj["y"], None])
        coords["z"].extend([ni["z"], nj["z"], None])

    for member_type, coords in grouped_coords.items():
        fig.add_trace(
            go.Scatter3d(
                x=coords["x"],
                y=coords["y"],
                z=coords["z"],
                mode="lines",
                name=member_type,
                line={"color": colors.get(member_type, "#444444"), "width": 5},
                hovertemplate=f"{member_type}<extra></extra>",
            )
        )

    ordered_node_ids = sorted(model.nodes)
    fig.add_trace(
        go.Scatter3d(
            x=[model.nodes[node_id]["x"] for node_id in ordered_node_ids],
            y=[model.nodes[node_id]["y"] for node_id in ordered_node_ids],
            z=[model.nodes[node_id]["z"] for node_id in ordered_node_ids],
            mode="markers+text" if show_node_ids else "markers",
            text=[str(node_id) for node_id in ordered_node_ids] if show_node_ids else None,
            textposition="top center",
            name="nodes",
            marker={"size": 3.5, "color": "#222222"},
            hovertemplate="Node %{text}<br>x=%{x:.2f}<br>y=%{y:.2f}<br>z=%{z:.2f}<extra></extra>"
            if show_node_ids
            else "x=%{x:.2f}<br>y=%{y:.2f}<br>z=%{z:.2f}<extra></extra>",
        )
    )

    fig.update_layout(
        title="Radial Multistory Structural Model",
        scene={
            "xaxis_title": "X [m]",
            "yaxis_title": "Y [m]",
            "zaxis_title": "Z [m]",
            "aspectmode": "data",
        },
        legend={"orientation": "h", "yanchor": "bottom", "y": 1.02, "xanchor": "left", "x": 0},
        margin={"l": 0, "r": 0, "t": 60, "b": 0},
    )
    return fig


if __name__ == "__main__":
    building = RadialBuilding(core_diameter=15, diameter=30, inner_ring_count=1, n_slices=13)
    model = building.generate()
    fig = plot_model_3d(model)
    fig.write_html("radial_multistory_model.html")
    fig.show()
